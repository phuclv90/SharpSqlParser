using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StringTool
{
    public static class StringUtil
    {
        public static bool EqualsIgnoreCase(this string left, string right)
        {
            return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}

namespace SqlZerteiler
{
    // Represents the input character stream. Allows for easy peeking (view the next character
    // without moving the file pointer)
    [DebuggerDisplay("Current char: {(char)currentChar}, pos: {inStream.Position}")]
    public class InputStream
    {
        // Common constants
        public const int EOF = -1;
        public const int NONE = -2;

        // Internal members
        private readonly Stream inStream;
        private int currentChar = NONE;

        public InputStream(Stream inputStream)
        {
            // currentChar = NONE;
            inStream = inputStream;
        }

        /// <summary>Check if we're at the end of stream</summary>
        public bool IsEof()
        {
            return Peek() == EOF;
        }

        /// <summary>Peek the next character without removing it from the stream</summary>
        public int Peek()
        {
            if (currentChar == NONE)
                currentChar = inStream.ReadByte();
            return currentChar;
        }

        /// <summary>Return the next character and advance the pointer in the stream</summary>
        /// <returns>The next character in the stream</returns>
        public int Next()
        {
            var c = currentChar;
            currentChar = NONE;
            if (c == NONE)
            {
                c = inStream.ReadByte();
            }
            return c;
        }

        /// <summary>Skip the next character and return the one after it</summary>
        /// <returns>The next character in the stream</returns>
        public int Skip()
        {
            Next();
            return Next();
        }

        /// <summary>Skip the next character and peek the one after it</summary>
        /// <returns>The next character in the stream</returns>
        public int SkipAndPeek()
        {
            Next();
            return Peek();
        }

        /// <summary>Go back <paramref name="numberOfCharacters"/> characters</summary>
        public void Back(long numberOfCharacters = 1)
        {
            currentChar = NONE;
            inStream.Seek(-numberOfCharacters, SeekOrigin.Current);
        }

        /// <summary>Current position in stream</summary>
        public long GetPos()
        {
            return inStream.Position;
        }
    }

    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("SqlZerteiler");

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: {0} <SQL file>", Environment.GetCommandLineArgs()[0]);
                return 1;
            }

            using (var inputFile = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    var inputStream = new InputStream(inputFile);
                    QueryParser parser = new QueryParser(new Tokenizer(inputStream));
                    parser.Parse();
                    Console.WriteLine("Done parsing!\n--------------------------------");
                    parser.Dump();
                    Console.WriteLine("-----------------------------------------------");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception: " + e.Message);
                }
            }

            return 0;
        }
    }
}
