using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Odinsons.ValheimLauncher
{
    /// <summary>The registry public keys a launcher trusts, by key id. ECDSA P-256, stored as
    /// DER SubjectPublicKeyInfo.</summary>
    public sealed class RegistryKeySet
    {
        private readonly Dictionary<string, byte[]> _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public RegistryKeySet(IEnumerable<KeyValuePair<string, byte[]>> keys)
        {
            foreach (KeyValuePair<string, byte[]> key in keys)
            {
                using ECDsa ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(key.Value, out _);
                if (ecdsa.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                    throw new ArgumentException($"Key '{key.Key}' is not a P-256 key.");
                _keys[key.Key] = key.Value;
            }
        }

        /// <summary>The keys compiled into this launcher.</summary>
        public static RegistryKeySet Official { get; } =
            new RegistryKeySet(Array.Empty<KeyValuePair<string, byte[]>>());

        public bool Verify(string keyId, byte[] data, byte[] signature)
        {
            if (!_keys.TryGetValue(keyId, out byte[] spki))
                throw new RegistryException(RegistryFailure.UnknownKey, $"Unknown registry key '{keyId}'.");

            try
            {
                using ECDsa ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
                return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
