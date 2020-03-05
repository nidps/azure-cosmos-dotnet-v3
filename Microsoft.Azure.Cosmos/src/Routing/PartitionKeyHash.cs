﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Routing
{
    using System;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.Azure.Cosmos.Sql;
    using Microsoft.Azure.Documents.Routing;

    /// <summary>
    /// There are many kinds of documents partitioning schemes (Range, Hash, Range+Hash, Hash+Hash etc.)
    /// All these partitioning schemes are abstracted by effective partition key.
    /// Effective partition key is just BYTE*. There is function which maps partition key to effective partition key based on partitioning scheme used.
    /// In case of range partitioning effective partition key corresponds one-to-one to partition key extracted from the document and relationship between effective partition keys is the same as relationship between partitionkeys from which they were calculated.
    /// In case of Hash partitioning, values of all paths are hashed together and resulting hash is prepended to the partition key.
    /// We have single global index on [effective partition key + name]
    /// </summary>
    /// <example>
    /// With the following definition:
    ///     "partitionKey" : {"paths":["/address/country", "address/zipcode"], "kind" : "Hash"}
    /// partition key ["USA", 98052] corresponds to effective partition key binaryencode([243451234, "USA", 98052]), where
    /// 243451234 is hash of "USA" and 98052 combined together.
    /// 
    /// With the following definition:
    ///     "partitionKey" : {"paths":["/address/country", "address/zipcode"], "kind" : "Range"}
    /// partition key ["USA", 98052] corresponds to effective partition key binaryencode(["USA", 98052]).
    /// </example>
    internal readonly struct PartitionKeyHash : IComparable<PartitionKeyHash>, IEquatable<PartitionKeyHash>
    {
        public PartitionKeyHash(UInt128 value)
        {
            this.Value = value;
        }

        public UInt128 Value { get; }

        public int CompareTo(PartitionKeyHash other)
        {
            return this.Value.CompareTo(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is PartitionKeyHash effectivePartitionKey))
            {
                return false;
            }

            if (object.ReferenceEquals(this, obj))
            {
                return true;
            }

            return this.Equals(effectivePartitionKey);
        }

        public bool Equals(PartitionKeyHash other)
        {
            return this.Value.Equals(other.Value);
        }

        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }

        public static class V1
        {
            private const int MaxStringLength = 100;

            private static readonly PartitionKeyHash True = PartitionKeyHash.V1.Hash(new byte[] { (byte)PartitionKeyComponentType.True });
            private static readonly PartitionKeyHash False = PartitionKeyHash.V1.Hash(new byte[] { (byte)PartitionKeyComponentType.False });
            private static readonly PartitionKeyHash Null = PartitionKeyHash.V1.Hash(new byte[] { (byte)PartitionKeyComponentType.Null });
            private static readonly PartitionKeyHash Undefined = PartitionKeyHash.V1.Hash(new byte[] { (byte)PartitionKeyComponentType.Undefined });
            private static readonly PartitionKeyHash EmptyString = PartitionKeyHash.V1.Hash(new byte[] { (byte)PartitionKeyComponentType.String });

            public static PartitionKeyHash Hash(bool boolean)
            {
                return boolean ? PartitionKeyHash.V1.True : PartitionKeyHash.V1.False;
            }

            public static PartitionKeyHash Hash(double value)
            {
                Span<byte> bytesForHashing = stackalloc byte[sizeof(byte) + sizeof(double)];
                bytesForHashing[0] = (byte)PartitionKeyComponentType.Number;
                MemoryMarshal.Cast<byte, double>(bytesForHashing.Slice(start: 1))[0] = value;
                return PartitionKeyHash.V1.Hash(bytesForHashing);
            }

            public static PartitionKeyHash Hash(string value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (value.Length == 0)
                {
                    return EmptyString;
                }

                ReadOnlySpan<char> trimmedValue = value.AsSpan(
                    start: 0,
                    length: Math.Min(value.Length, MaxStringLength));

                Span<byte> bytesForHashing = stackalloc byte[sizeof(byte) + Encoding.UTF8.GetByteCount(trimmedValue)];
                bytesForHashing[0] = (byte)PartitionKeyComponentType.String;
                Span<byte> bytesForHashingSuffix = bytesForHashing.Slice(start: 1);
                Encoding.UTF8.GetBytes(
                    trimmedValue,
                    bytesForHashingSuffix);
                return PartitionKeyHash.V1.Hash(bytesForHashing);
            }

            public static PartitionKeyHash HashNull()
            {
                return PartitionKeyHash.V1.Null;
            }

            public static PartitionKeyHash HashUndefined()
            {
                return PartitionKeyHash.V1.Undefined;
            }

            private static PartitionKeyHash Hash(ReadOnlySpan<byte> bytesForHashing)
            {
                uint hash = Cosmos.MurmurHash3.Hash32(bytesForHashing, seed: 0);
                return new PartitionKeyHash(hash);
            }
        }

        public static class V2
        {
            private const int MaxStringLength = 2 * 1024;
            private static readonly PartitionKeyHash True = PartitionKeyHash.V2.Hash(new byte[] { (byte)PartitionKeyComponentType.True });
            private static readonly PartitionKeyHash False = PartitionKeyHash.V2.Hash(new byte[] { (byte)PartitionKeyComponentType.False });
            private static readonly PartitionKeyHash Null = PartitionKeyHash.V2.Hash(new byte[] { (byte)PartitionKeyComponentType.Null });
            private static readonly PartitionKeyHash Undefined = PartitionKeyHash.V2.Hash(new byte[] { (byte)PartitionKeyComponentType.Undefined });
            private static readonly PartitionKeyHash EmptyString = PartitionKeyHash.V2.Hash(new byte[] { (byte)PartitionKeyComponentType.String });

            public static PartitionKeyHash Hash(bool boolean)
            {
                return boolean ? PartitionKeyHash.V2.True : PartitionKeyHash.V2.False;
            }

            public static PartitionKeyHash Hash(double value)
            {
                Span<byte> bytesForHashing = stackalloc byte[sizeof(byte) + sizeof(double)];
                bytesForHashing[0] = (byte)PartitionKeyComponentType.Number;
                MemoryMarshal.Cast<byte, double>(bytesForHashing.Slice(start: 1))[0] = value;
                return PartitionKeyHash.V2.Hash(bytesForHashing);
            }

            public static PartitionKeyHash Hash(string value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (value.Length > MaxStringLength)
                {
                    throw new ArgumentOutOfRangeException($"{nameof(value)} is too long.");
                }

                if (value.Length == 0)
                {
                    return EmptyString;
                }

                Span<byte> bytesForHashing = stackalloc byte[sizeof(byte) + Encoding.UTF8.GetByteCount(value)];
                bytesForHashing[0] = (byte)PartitionKeyComponentType.String;
                Span<byte> bytesForHashingSuffix = bytesForHashing.Slice(start: 1);
                Encoding.UTF8.GetBytes(value, bytesForHashingSuffix);
                return PartitionKeyHash.V2.Hash(bytesForHashing);
            }

            public static PartitionKeyHash HashNull()
            {
                return PartitionKeyHash.V2.Null;
            }

            public static PartitionKeyHash HashUndefined()
            {
                return PartitionKeyHash.V2.Undefined;
            }

            private static PartitionKeyHash Hash(ReadOnlySpan<byte> bytesForHashing)
            {
                UInt128 hash = Cosmos.MurmurHash3.Hash128(bytesForHashing, seed: 0);
                return new PartitionKeyHash(hash);
            }
        }
    }
}