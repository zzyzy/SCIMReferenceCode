// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    // Microsoft.SCIM declares its own internal JsonSerializer, which shadows Newtonsoft's.
    using NewtonsoftSerializer = Newtonsoft.Json.JsonSerializer;

    /// <summary>
    /// Makes the untyped schema extensions of an <see cref="IExtensibleResource"/> - a user
    /// or a group - round-trip over HTTP.
    /// </summary>
    /// <remarks>
    /// Without this, <c>CustomExtension</c> is unreachable from the wire in both directions.
    /// Outbound, the only code that emits it is <c>ToJson()</c>, which nothing in either
    /// hosting leg calls - responses are serialized straight off the object by Newtonsoft,
    /// which reads <c>[DataMember]</c> and never consults <see cref="IJsonSerializable"/>.
    /// Inbound, the only code that fills it is
    /// <c>Core2EnterpriseUserJsonDeserializingFactory</c>, which the hosts also bypass because
    /// MVC model binding produces the typed body instead.
    ///
    /// It matches on <see cref="IExtensibleResource"/> rather than on the user type: groups
    /// hold extensions the same way, and matching the user alone dropped every extension a
    /// client sent on a group while accepting the request.
    ///
    /// This converter closes both halves at the one place both legs share - the Newtonsoft
    /// settings - so an extension namespace the service was never compiled against still
    /// survives a POST and comes back on a GET.
    ///
    /// It does not go through <c>ToJson()</c>. That method runs a
    /// <c>DataContractJsonSerializer</c> into a stream, reads the string back and re-parses it
    /// into a dictionary; making it the response path would put a serialize-then-reparse round
    /// trip on every response and leave two serializers to keep aligned on dates, enums and
    /// nulls. Newtonsoft stays the single serializer; this only adds the members it cannot see.
    ///
    /// A typed extension member wins over the dictionary. <see cref="EduPassUser"/>-style
    /// subclasses declare their extension as a real <c>[DataMember]</c>, so on read those
    /// properties are already bound and are not duplicated into the dictionary, and on write the
    /// dictionary never overwrites them.
    /// </remarks>
    public sealed class SchematizedJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return null != objectType && typeof(IExtensibleResource).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, NewtonsoftSerializer serializer)
        {
            if (null == writer)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (null == value)
            {
                writer.WriteNull();
                return;
            }

            IExtensibleResource resource = (IExtensibleResource)value;

            // Serialized by a serializer that does not carry this converter, so the default
            // contract runs and there is no recursion.
            JObject json = JObject.FromObject(value, SchematizedJsonConverter.WithoutSelf(serializer));

            foreach (KeyValuePair<string, IDictionary<string, object>> entry in resource.CustomExtension)
            {
                // A typed member for the same schema URI has already written it.
                if (null != json.Property(entry.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                json.Add(entry.Key, JObject.FromObject(entry.Value));
            }

            json.WriteTo(writer);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            NewtonsoftSerializer serializer)
        {
            if (null == reader)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            JToken token = JToken.Load(reader);

            if (token.Type == JTokenType.Null)
            {
                return null;
            }

            if (!(token is JObject json))
            {
                throw new JsonSerializationException(
                    SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidResource);
            }

            object result = json.ToObject(objectType, SchematizedJsonConverter.WithoutSelf(serializer));

            if (result is IExtensibleResource resource)
            {
                SchematizedJsonConverter.ReadCustomExtensions(json, objectType, resource, serializer);
            }

            return result;
        }

        private static void ReadCustomExtensions(
            JObject json,
            Type objectType,
            IExtensibleResource resource,
            NewtonsoftSerializer serializer)
        {
            // The names the default contract already bound, so a typed extension member is not
            // also captured as an untyped one - which would emit the same key twice on write.
            HashSet<string> bound =
                new HashSet<string>(
                    SchematizedJsonConverter
                        .WithoutSelf(serializer)
                        .ContractResolver
                        .ResolveContract(objectType) is Newtonsoft.Json.Serialization.JsonObjectContract contract
                            ? contract.Properties.Select(property => property.PropertyName)
                            : Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (JProperty property in json.Properties())
            {
                if (bound.Contains(property.Name))
                {
                    continue;
                }

                if (!property.Name.StartsWith(SchemaIdentifiers.PrefixExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!(property.Value is JObject nested))
                {
                    continue;
                }

                // AddCustomAttribute itself rejects anything that is not an extension URI, and
                // skips the enterprise extension - which is a typed member on
                // Core2EnterpriseUserBase - so it is not defeated by being handed one.
                resource.AddCustomAttribute(
                    property.Name,
                    SchematizedJsonConverter.ToDictionary(nested));
            }
        }

        /// <summary>
        /// A serializer with the same settings as <paramref name="serializer"/> but without this
        /// converter, so nested calls use the default contract.
        /// </summary>
        private static NewtonsoftSerializer WithoutSelf(NewtonsoftSerializer serializer)
        {
            if (null == serializer)
            {
                return NewtonsoftSerializer.CreateDefault();
            }

            NewtonsoftSerializer result =
                new NewtonsoftSerializer
                {
                    ContractResolver = serializer.ContractResolver,
                    NullValueHandling = serializer.NullValueHandling,
                    DefaultValueHandling = serializer.DefaultValueHandling,
                    DateFormatHandling = serializer.DateFormatHandling,
                    DateTimeZoneHandling = serializer.DateTimeZoneHandling,
                    DateParseHandling = serializer.DateParseHandling,
                    FloatParseHandling = serializer.FloatParseHandling,
                    StringEscapeHandling = serializer.StringEscapeHandling,
                    MissingMemberHandling = serializer.MissingMemberHandling,
                    ReferenceLoopHandling = serializer.ReferenceLoopHandling,
                    TypeNameHandling = TypeNameHandling.None,
                    Culture = serializer.Culture,
                    MaxDepth = serializer.MaxDepth,
                };

            foreach (JsonConverter converter in serializer.Converters)
            {
                if (!(converter is SchematizedJsonConverter))
                {
                    result.Converters.Add(converter);
                }
            }

            return result;
        }

        /// <summary>
        /// Converts a parsed extension object into the shape
        /// <see cref="IExtensibleResource.AddCustomAttribute"/> accepts, which is a concrete
        /// <see cref="Dictionary{TKey, TValue}"/> and nothing else.
        /// </summary>
        private static Dictionary<string, object> ToDictionary(JObject json)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            foreach (JProperty property in json.Properties())
            {
                result.Add(property.Name, SchematizedJsonConverter.ToValue(property.Value));
            }

            return result;
        }

        private static object ToValue(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    return SchematizedJsonConverter.ToDictionary((JObject)token);
                case JTokenType.Array:
                    return ((JArray)token).Select(SchematizedJsonConverter.ToValue).ToList();
                case JTokenType.Null:
                    return null;
                default:
                    return ((JValue)token).Value;
            }
        }
    }
}
