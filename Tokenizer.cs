using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SqlZerteiler
{
    using StringTool;
    using System.Diagnostics;

    /// <summary>Represent each token in the language</summary>
    [DebuggerDisplay("{value} ({type})")]
    public class Token : IEquatable<Token>
    {
        public enum Type
        {
            Keyword,
            Identifier,
            Operator,
            Punctuation,
            Integer,    // for all kinds of integers from small to big
            Float,      // for all floating-point types
            Boolean,
            String,
            Invalid
        }

        public Type type { get; private set; }
        public string value { get; private set; }

        public Token(Type type, string value)
        {
            this.type = type;
            this.value = value;
        }

        public bool Equals([AllowNull] Token other)
        {
            if (other == null)
                return false;
            return type == other.type && value.EqualsIgnoreCase(other.value);
        }

        public static bool operator ==(Token token1, Token token2)
        {
            if (ReferenceEquals(token1, token2))
            {
                return true;
            }
            if (ReferenceEquals(token1, null))
            {
                return false;
            }
            if (ReferenceEquals(token2, null))
            {
                return false;
            }

            return token1.Equals(token2);
        }

        public static bool operator !=(Token token1, Token token2)
        {
            return !(token1 == token2);
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;
            return obj is Token && Equals((Token)obj);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode() ^ type.GetHashCode();
        }
        public override string ToString()
        {
            return "'" + value + "' (" + type + ")";
        }

        public bool IsOperator(string op)
        {
            return type == Type.Operator && value.EqualsIgnoreCase(op);
        }

        public bool IsPunctuation(string op)
        {
            return type == Type.Punctuation && value.EqualsIgnoreCase(op);
        }

        public bool IsKeyword(string keyword)
        {
            return type == Type.Keyword && value.EqualsIgnoreCase(keyword);
        }

        public bool IsIdentifier(string identifier)
        {
            return type == Type.Identifier && value.EqualsIgnoreCase(identifier);
        }
    }

    /// <summary>Split the input stream into tokens</summary>
    [DebuggerDisplay("Current token: {currentToken}, pos: {inputStream.GetPos}")]

    public class Tokenizer
    {
        private readonly InputStream inputStream;
        private Token currentToken;

        public Tokenizer(InputStream inputStream)
        {
            this.inputStream = inputStream;
            // Peek();
        }

        private bool IsWhiteSpace(int ch)
        {
            return ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
        }

        // ----- Handle operators
        /// <summary>Maps a character that's used in operators to whether
        /// they can start a multi-character operator</summary>
        private static readonly Dictionary<char, bool> OperatorChar = new Dictionary<char, bool> {
            { '+', false },
            { '-', false },
            { '*', false },
            { '/', false },
            { '%', false },
            { '<', true  }, // < can be the start of <=, <> or <<
            { '=', true  }, // = can be the start of ==
            { '>', true  }, // < can be the start of >= or >>
            { '!', true  }, // ! can be the start of !=
            { '|', true  }, // | can be the start of ||
            { '&', false },
        };
        /// <summary>Check if the input character can appear in operators</summary>
        private bool IsOperatorChar(int ch)
        {
            return OperatorChar.ContainsKey((char)ch);
        }
        /// <summary>Read operators made from special characters only</summary>
        private Token ReadOperator()
        {
            char firstOperatorChar = (char)inputStream.Next();
            string op = firstOperatorChar.ToString();
            bool isMultiCharacterOperator;
            if (OperatorChar.TryGetValue(firstOperatorChar, out isMultiCharacterOperator)
                && isMultiCharacterOperator)
            {
                char secondOperatorChar = (char)inputStream.Peek();
                switch (firstOperatorChar)
                {
                    case '<':
                        if (secondOperatorChar == '<' || secondOperatorChar == '=' || secondOperatorChar == '>')
                        {
                            op += secondOperatorChar;
                            inputStream.Next();
                        }
                        break;
                    case '>':
                        if (secondOperatorChar == '>' || secondOperatorChar == '=')
                        {
                            op += secondOperatorChar;
                            inputStream.Next();
                        }
                        break;
                    case '=':
                        if (secondOperatorChar == '=')
                        {
                            op += secondOperatorChar;
                            inputStream.Next();
                        }
                        break;
                    case '!':
                        if (secondOperatorChar == '=')
                        {
                            op += secondOperatorChar;
                            inputStream.Next();
                            break;
                        }
                        else
                            throw ReportError("Invalid operator !");
                    case '|':
                        if (secondOperatorChar == '|')
                        {
                            op += secondOperatorChar;
                            inputStream.Next();
                        }
                        break;
                    default:
                        throw ReportError("Invalid operator !");
                }
            }
            return new Token(Token.Type.Operator, op);
        }

        private bool IsPunctuationChar(int ch)
        {
            return ch == '.' || ch == ',' ||
                   ch == '(' || ch == ')' || ch == ';';
        }
        private Token ReadPunctuation()
        {
            char punct = (char)inputStream.Next();
            return new Token(Token.Type.Punctuation, punct.ToString());
        }

        // ----- Handle numbers
        private bool IsDigit(int ch)
        {
            return ch >= '0' && ch <= '9';
        }
        private Token ReadNumber()
        {
            int sign = inputStream.Peek();
            var value = ReadWhile(IsDigit);
            int dot = inputStream.Peek();
            var type = Token.Type.Integer;
            if (dot == '.')
            {
                value.Append('.');
                type = Token.Type.Float;

                var fractionalDigit = inputStream.SkipAndPeek();
                string exponent = string.Empty;
                if (IsDigit(fractionalDigit))
                {
                    value.Append(ReadWhile(IsDigit));
                    var eChar = inputStream.Peek();
                    if (eChar == 'e' || eChar == 'E')
                    {
                        var expSign = inputStream.SkipAndPeek();
                        if (expSign == '+' || expSign == '-')
                        {
                            exponent += (char)expSign;
                            inputStream.Next();
                        }
                        char expDigit = (char)inputStream.Peek();
                        if (!IsDigit(expDigit) && !IsPunctuationChar(expDigit))
                            throw ReportError("Invalid floating-point exponent");
                        exponent = (char)eChar + exponent;
                        value.Append(exponent);
                        value.Append(ReadWhile(IsDigit));
                    }
                }
            }
            return new Token(type, value.ToString());
        }

        // ----- Handle identifiers and keywords
        /// <summary>Check if a character is a valid character in identifiers</summary>
        /// <param name="ch">The code point to check</param>
        /// <param name="startCharacter">If true, returns whether the character is valid at the start
        /// of an identifier, otherwise returns true if the character is valid at the remaining positions
        /// </param>
        /// <returns>True if the value is a character used in identifiers</returns>
        private bool IsIdentifier(int ch, bool startCharacter = false)
        {
            bool isAlphaUnderscore = ('a' <= ch && ch <= 'z') || ('A' <= ch && ch <= 'Z') || (ch == '_');
            if (startCharacter)
            {
                return isAlphaUnderscore;
            }
            else
            {
                return isAlphaUnderscore || (ch == '.')
                                         || ('0' <= ch && ch <= '9')
                                         || (ch == '_') || (!startCharacter && ch == '.');
            }
        }

        /// <summary>All the individual keywords in SQL</summary>
        private static readonly HashSet<string> Keywords = new HashSet<string>(new List<string>
        {
            "USE", "SELECT", "WHERE", "IS", "NOT", "NULL", "GROUP", "ORDER", "BY",
            "INSERT", "VALUES", "DELETE",
            "FROM", "INTO",
            "CASE", "WHEN",
            "LIKE",
            "BETWEEN", "AND",
            "AS",
            "DISTINCT",
            "TRUE", "FALSE", "NULL",
            "ASC", "DESC"
        }, StringComparer.OrdinalIgnoreCase);
        private bool IsKeyword(string str)
        {
            return Keywords.Contains(str);
        }

        // ----- Read token string functions
        private Token ReadNext()
        {
            SkipWhitespaces();

            if (inputStream.IsEof())
            {
                return null;
            }

            int nextByte = inputStream.Peek();
            if (nextByte == '"' || nextByte == '\'')
                return ReadString();
            if (IsDigit(nextByte))
                return ReadNumber();
            if (IsOperatorChar(nextByte))
                return ReadOperator();
            if (IsPunctuationChar(nextByte))
                return ReadPunctuation();
            if (IsIdentifier(nextByte, true))
                return ReadIdentifier();

            return null;
        }

        /// <summary>Read the string until the first character that doesn't match the current condition</summary>
        /// <param name="condition"></param>
        private string ReadStringWhile(Predicate<int> condition)
        {
            return ReadWhile(condition).ToString();
        }

        /// <summary>Read the string until the first character that doesn't match the current condition</summary>
        /// <param name="condition"></param>
        private StringBuilder ReadWhile(Predicate<int> condition)
        {
            StringBuilder value = new StringBuilder();
            while (!inputStream.IsEof() && condition(inputStream.Peek()))
            {
                value.Append((char)inputStream.Next());
            }

            return value;
        }

        /// <summary>Read identifiers or keywords</summary>
        /// <returns>The token containing the identifier or keyword</returns>
        private Token ReadIdentifier()
        {
            var token = ReadWhile(ch => IsIdentifier(ch));
            var tokenString = token.ToString();
            if (tokenString == "IS")
            {
                SkipWhitespaces();
                if (inputStream.Peek() == 'N')
                {
                    var nextToken = Peek();
                    if (nextToken.value.EqualsIgnoreCase("NOT") && nextToken.type == Token.Type.Keyword)
                    {
                        token.Append(" NOT");
                        return new Token(Token.Type.Operator, token.ToString());
                    }
                    else
                    {
                        // Return the token
                        currentToken = null;
                        for (int i = 0; i < nextToken.value.Length; i++)
                            inputStream.Back();
                    }
                }
            }
            else
            {
                // Asterisk * can appear at the end of the identifier like db.*
                if (tokenString.EndsWith(".") && inputStream.Peek() == '*')
                {
                    tokenString = token.Append('*').ToString();
                }
            }

            if (IsKeyword(tokenString))
            {
                if (QueryParser.IsWordOperator(tokenString))
                    return new Token(Token.Type.Operator, tokenString);
                if (tokenString.EqualsIgnoreCase("TRUE") || tokenString.EqualsIgnoreCase("FALSE"))
                    return new Token(Token.Type.Boolean, tokenString);
                return new Token(Token.Type.Keyword, tokenString);
            }
            return new Token(Token.Type.Identifier, tokenString);
        }

        private static Dictionary<char, char> EscapeCharacter = new Dictionary<char, char>
        {
            ['n'] = '\n',
            ['r'] = '\r',
            ['t'] = '\t',
            ['0'] = '\0',
            ['\''] = '\'',
            ['\"'] = '\"',
        };
        private Token ReadString()
        {
            StringBuilder value = new StringBuilder();
            char quote = (char)inputStream.Next(); // swallow the first quote character
            bool escaped = false;
            while (inputStream.Peek() != InputStream.EOF)
            {
                char nextChar = (char)inputStream.Next();
                if (escaped)
                {
                    if (EscapeCharacter.TryGetValue(nextChar, out char realChar))
                        value.Append(realChar);
                    else
                        value.Append(nextChar);

                    escaped = false;
                }
                else if (nextChar == '\\')
                {
                    escaped = true;
                }
                else if (nextChar == quote)
                {
                    // 2 consecutive quote characters inside the same quoted string will escape the quote
                    if (inputStream.Peek() == quote)
                    {
                        inputStream.Next();
                        value.Append(quote);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    value.Append(nextChar);
                }
            }
            return new Token(Token.Type.String, value.ToString());
        }

        private void SkipWhitespaces()
        {
            ReadWhile(IsWhiteSpace);
            if (inputStream.Peek() != '-')
                return;
            if (inputStream.SkipAndPeek() == '-')
            {
                SkipComment();
                ReadWhile(IsWhiteSpace);
            }
            else
            {
                inputStream.Back(2);
            }
        }

        private void SkipComment()
        {
            // The first `-` has been swallowed
            inputStream.Next();
            ReadWhile(c => c != '\n');
        }

        public Token Next()
        {
            var token = currentToken;
            currentToken = null;
            return token ?? ReadNext();
        }

        public Token Peek()
        {
            return currentToken ?? (currentToken = ReadNext());
        }

        public Token Skip()
        {
            Next();
            return Next();
        }

        public Token SkipAndPeek()
        {
            Next();
            return Peek();
        }

        public bool IsEof { get { return Peek() == null; } }

        public long GetPos()
        {
            return inputStream.GetPos();
        }

        private TokeningException ReportError(string error)
        {
            return new TokeningException(error, GetPos());
        }

        class TokeningException : Exception
        {
            public TokeningException()
            {
            }

            public TokeningException(string message, long pos) :
                base("Error at offset " + pos + ": " + message)
            {
            }

            public TokeningException(string message, long pos, Exception inner) :
                base("Error at offset " + pos + ": " + message, inner)
            {
            }
        }
    }
}

