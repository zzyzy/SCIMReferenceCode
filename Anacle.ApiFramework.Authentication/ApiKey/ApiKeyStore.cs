// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Anacle.ApiFramework.Authentication.ApiKey
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Claims;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The caller a presented API key resolves to.
    /// </summary>
    public sealed class ApiKeyIdentity
    {
        public ApiKeyIdentity(string name, IEnumerable<Claim> claims = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A name is required.", nameof(name));
            }

            this.Name = name;
            this.Claims = (claims ?? Enumerable.Empty<Claim>()).ToArray();
        }

        /// <summary>The caller's identity. Never the key itself.</summary>
        public string Name
        {
            get;
        }

        /// <summary>Any further claims the caller carries, such as a role or a tenant.</summary>
        public IReadOnlyCollection<Claim> Claims
        {
            get;
        }
    }

    /// <summary>
    /// Resolves a presented API key to a caller.
    /// </summary>
    /// <remarks>
    /// Deliberately an interface rather than a list of keys in configuration. Where keys live,
    /// how they are provisioned and how they are revoked are decisions this library has no
    /// business making. Implementations must return null for an unknown key rather than
    /// throwing, and must not log the presented value.
    /// </remarks>
    public interface IApiKeyStore
    {
        Task<ApiKeyIdentity> ResolveAsync(string presentedKey, CancellationToken cancel);
    }

    /// <summary>
    /// An <see cref="IApiKeyStore"/> over a fixed set of keys held as SHA-256 hashes.
    /// </summary>
    /// <remarks>
    /// Suitable for a small, static set of callers. Two properties matter and both are easy to
    /// get wrong by hand:
    ///
    /// The keys are hashed, so the plaintext is not held in memory for the process lifetime and
    /// does not appear in a crash dump. Comparison is over the fixed-length hashes, so the time
    /// taken does not vary with how much of the key was guessed correctly - an ordinary string
    /// comparison returns as soon as two bytes differ, which leaks the key one character at a
    /// time to a caller who can measure it.
    ///
    /// A store backed by a database should hash the same way and compare the same way.
    /// </remarks>
    public sealed class HashedApiKeyStore : IApiKeyStore
    {
        private readonly IReadOnlyCollection<Entry> entries;

        /// <param name="keys">Caller name to plaintext key. Hashed on construction.</param>
        public HashedApiKeyStore(IEnumerable<KeyValuePair<string, string>> keys)
        {
            if (null == keys)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            this.entries =
                keys
                    .Select(
                        pair =>
                            new Entry
                            {
                                Identity = new ApiKeyIdentity(pair.Key),
                                Hash = HashedApiKeyStore.Hash(pair.Value),
                            })
                    .ToArray();
        }

        public Task<ApiKeyIdentity> ResolveAsync(string presentedKey, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(presentedKey))
            {
                return Task.FromResult<ApiKeyIdentity>(null);
            }

            byte[] presented = HashedApiKeyStore.Hash(presentedKey);
            ApiKeyIdentity result = null;

            // Every entry is compared even after a match, so that the time taken does not
            // reveal the position of the matching key.
            foreach (Entry entry in this.entries)
            {
                if (HashedApiKeyStore.FixedTimeEquals(presented, entry.Hash))
                {
                    result = entry.Identity;
                }
            }

            return Task.FromResult(result);
        }

        private static byte[] Hash(string value)
        {
            if (null == value)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (SHA256 algorithm = SHA256.Create())
            {
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
            }
        }

        /// <summary>
        /// Compares two equal-length byte arrays in time independent of their contents.
        /// </summary>
        /// <remarks>
        /// .NET Framework has no <c>CryptographicOperations.FixedTimeEquals</c>, and this
        /// library multi-targets it, so the comparison is written out. The loop must not exit
        /// early - that is the entire point.
        /// </remarks>
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (null == left || null == right || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;

            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return 0 == difference;
        }

        private sealed class Entry
        {
            public ApiKeyIdentity Identity
            {
                get;
                set;
            }

            public byte[] Hash
            {
                get;
                set;
            }
        }
    }
}
