using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.SCIM
{
    public class HttpHeaders : Dictionary<string, string[]>
    {
        public HttpHeaders() : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public void Add(string name, string value)
        {
            if (TryGetValue(name, out var existingValues))
            {
                this[name] = existingValues.Concat(new[] { value }).ToArray();
            }
            else
            {
                this[name] = new[] { value };
            }
        }

        public string Location
        {
            get => GetFirstValue(nameof(Location));
            set => this[nameof(Location)] = new[] { value };
        }

        private string GetFirstValue(string name)
        {
            if (TryGetValue(name, out var values))
            {
                return values.FirstOrDefault();
            }

            return default;
        }
    }
}