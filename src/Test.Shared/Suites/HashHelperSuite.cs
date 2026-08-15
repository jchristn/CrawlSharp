namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Text;
    using CrawlSharp.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for <see cref="HashHelper"/> using published test vectors.
    /// </summary>
    public static class HashHelperSuite
    {
        private const string Id = "HashHelper";

        private const string Md5Empty = "D41D8CD98F00B204E9800998ECF8427E";
        private const string Md5Abc = "900150983CD24FB0D6963F7D28E17F72";
        private const string Sha1Abc = "A9993E364706816ABA3E25717850C26C9CD0D89D";
        private const string Sha256Abc = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

        private static string Hex(byte[] bytes)
        {
            return Convert.ToHexString(bytes);
        }

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Md5_KnownVectors", "MD5 matches published vectors for empty and 'abc'", () =>
                {
                    Check.Equal(Md5Empty, Hex(HashHelper.MD5Hash("")));
                    Check.Equal(Md5Abc, Hex(HashHelper.MD5Hash("abc")));
                }),

                Case.Sync(Id, "Sha1_KnownVector", "SHA1 matches the published vector for 'abc'", () =>
                {
                    Check.Equal(Sha1Abc, Hex(HashHelper.SHA1Hash("abc")));
                }),

                Case.Sync(Id, "Sha256_KnownVector", "SHA256 matches the published vector for 'abc'", () =>
                {
                    Check.Equal(Sha256Abc, Hex(HashHelper.SHA256Hash("abc")));
                }),

                Case.Sync(Id, "ByteAndString_Agree", "Byte-array and string overloads agree for the same content", () =>
                {
                    byte[] fromBytes = HashHelper.SHA256Hash(Encoding.UTF8.GetBytes("abc"));
                    byte[] fromString = HashHelper.SHA256Hash("abc");
                    Check.BytesEqual(fromString, fromBytes);
                }),

                Case.Sync(Id, "NullBytes_HashesAsEmpty", "A null byte array hashes as empty content", () =>
                {
                    Check.Equal(Md5Empty, Hex(HashHelper.MD5Hash((byte[])null)));
                }),

                Case.Sync(Id, "EmptyBytes_HashesAsEmpty", "An empty byte array hashes as empty content", () =>
                {
                    Check.Equal(Md5Empty, Hex(HashHelper.MD5Hash(Array.Empty<byte>())));
                }),

                Case.Sync(Id, "NullString_HashesAsEmpty", "A null string hashes as empty content", () =>
                {
                    Check.Equal(Md5Empty, Hex(HashHelper.MD5Hash((string)null)));
                }),

                Case.Sync(Id, "Stream_MatchesBytes", "Stream hashing matches byte-array hashing", () =>
                {
                    byte[] data = Encoding.UTF8.GetBytes("abc");
                    using MemoryStream ms = new MemoryStream(data);
                    Check.Equal(Sha256Abc, Hex(HashHelper.SHA256Hash(ms)));
                }),

                Case.Sync(Id, "NullStream_Throws", "A null stream is rejected", () =>
                {
                    Check.Throws<ArgumentNullException>(() => HashHelper.MD5Hash((Stream)null));
                    Check.Throws<ArgumentNullException>(() => HashHelper.SHA1Hash((Stream)null));
                    Check.Throws<ArgumentNullException>(() => HashHelper.SHA256Hash((Stream)null));
                }),

                Case.Sync(Id, "StringList_Deterministic", "String-list hashing is deterministic", () =>
                {
                    List<string> a = new List<string> { "one", "two", "three" };
                    List<string> b = new List<string> { "one", "two", "three" };
                    Check.BytesEqual(HashHelper.SHA1Hash(a), HashHelper.SHA1Hash(b));
                }),

                Case.Sync(Id, "StringList_OrderMatters", "String-list hashing is order-sensitive", () =>
                {
                    List<string> a = new List<string> { "one", "two" };
                    List<string> b = new List<string> { "two", "one" };
                    Check.NotEqual(Hex(HashHelper.SHA1Hash(a)), Hex(HashHelper.SHA1Hash(b)));
                }),

                Case.Sync(Id, "StringList_NullOrEmpty_Empty", "A null or empty string list hashes to an empty array", () =>
                {
                    Check.Equal(0, HashHelper.MD5Hash((List<string>)null).Length);
                    Check.Equal(0, HashHelper.MD5Hash(new List<string>()).Length);
                }),

                Case.Sync(Id, "DataTable_Deterministic", "DataTable hashing is deterministic", () =>
                {
                    Check.BytesEqual(HashHelper.SHA256Hash(BuildTable()), HashHelper.SHA256Hash(BuildTable()));
                }),

                Case.Sync(Id, "DataTable_Null_HashesAsEmpty", "A null DataTable hashes as empty content", () =>
                {
                    Check.Equal(Md5Empty, Hex(HashHelper.MD5Hash((DataTable)null)));
                }),
            };

            return new TestSuiteDescriptor(Id, "Hashing helpers", cases);
        }

        private static DataTable BuildTable()
        {
            DataTable dt = new DataTable("t");
            dt.Columns.Add("id", typeof(int));
            dt.Columns.Add("name", typeof(string));
            dt.Rows.Add(1, "alpha");
            dt.Rows.Add(2, null);
            return dt;
        }
    }
}
