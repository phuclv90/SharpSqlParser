using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dumper
{
    using SqlZerteiler;
    public static class Dumper
    {
        public static void DumpList(this List<QueryParser.SqlNode> list,
            string itemName, string indentation)
        {
            Console.WriteLine(indentation + itemName);
            foreach (var column in list ?? Enumerable.Empty<QueryParser.SqlNode>())
            {
                column.Dump(null, indentation);
            }
        }
    }
}

namespace SqlZerteiler
{
    using StringTool;
    using System.Diagnostics;
    using Dumper;
    using System.Runtime.Remoting;

    // http://www.contrib.andrew.cmu.edu/~shadow/sql/sql1992.txt
    public class QueryParser
    {
        private readonly Tokenizer tokenizer;
        private RootNode rootNode;

        /// <summary>AST node types</summary>
        public enum Type
        {
            RootQueries,
            SelectStatement,
            InsertStatement,
            DeleteStatement,
            UpdateStatement,
            BinaryExpression,
            FunctionCall,
            Identifier,
            Literal             // Numbers, strings...
        }

        #region Private constants
        // Precedence for operators https://www.sqlite.org/lang_expr.html
        private static readonly Dictionary<string, int> Precedence = new Dictionary<string, int>(new Dictionary<string, int>
        {
            { "||",      9 },
            { "*",       8 },   { "/",      8 },    { "%",  8 },
            { "+",       7 },   { "-",      7 },
            { "<<",      6 },   { ">>",     6 },    { "&",  6 },    { "|",  6 },
            { "<",       5 },   { ">",      5 },    { "<=", 5 },    { ">=", 5 },
            { "=",       4 },   { "==",     4 },    { "!=", 4 },    { "<>", 4 },
            { "IS",      4 },   { "IS NOT", 4 },    { "IN", 4 },    { "LIKE", 4 },
            { "GLOB",    4 },   { "MATCH",  4 },    { "REGEXP", 4 },
            { "BETWEEN", 3 },   { "AND",    3 },    { "OR", 2 },
            { "AS",      1 },
        }, StringComparer.OrdinalIgnoreCase);

        //private static readonly Dictionary<string, int> Precedence = new Dictionary<string, int>
        private static readonly HashSet<string> WordOperator = new HashSet<string>(new List<string>
        {
            "IS", "IS NOT", "IN", "LIKE", "GLOB", "MATCH", "REGEXP",
            "BETWEEN", "AND", "OR", "AS"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly Token LeftParen = new Token(Token.Type.Punctuation, "(");
        private static readonly Token RightParen = new Token(Token.Type.Punctuation, ")");
        private static readonly Token Comma = new Token(Token.Type.Punctuation, ",");
        private static readonly Token Semicolon = new Token(Token.Type.Punctuation, ";");
        private static readonly Token From = new Token(Token.Type.Keyword, "FROM");
        private static readonly Token Asc = new Token(Token.Type.Keyword, "ASC");
        private static readonly Token Desc = new Token(Token.Type.Keyword, "DESC");
        private static readonly Token Where = new Token(Token.Type.Keyword, "WHERE");
        private static readonly Token Group = new Token(Token.Type.Keyword, "GROUP");
        private static readonly Token Having = new Token(Token.Type.Keyword, "HAVING");
        private static readonly Token Order = new Token(Token.Type.Keyword, "ORDER");

        private static readonly List<Token> FromStopCondition = new List<Token>
        {
            Where, Group, Having, Order, Semicolon
        };

        private static readonly List<Token> GroupByStopCondition = new List<Token>
        {
            Having, Order, Semicolon
        };

        private static readonly List<Token> OrderByStopCondition = new List<Token>
        {
            Asc, Desc, Semicolon
        };
        #endregion

        // ------------------------
        #region AST node for SQL queries
        public class SqlNode
        {
            public Type type { get; protected set; }
            public virtual void Dump(string itemName, string indentation)
            {
                if (itemName != null)
                    Console.WriteLine(indentation + itemName);
            }
        }

        /// <summary>Binary or unary expressions</summary>
        [DebuggerDisplay("Exp {left.type} {op} {right.type}")]
        class ExpressionNode : SqlNode
        {
            protected SqlNode left;
            protected string op;
            protected SqlNode right;    // null for unary expressions

            /// <summary>
            /// Create a new unary expression
            /// </summary>
            public ExpressionNode(SqlNode left, string op)
            {
                this.left = left;
                this.op = op;
                this.right = null;
            }

            /// <summary>
            /// Create a new binary expression
            /// </summary>
            public ExpressionNode(SqlNode left, string op, SqlNode right)
            {
                this.left = left;
                this.op = op;
                this.right = right;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                if (right != null)
                {
                    Console.WriteLine(indentation + "    Binary operator: " + op.ToUpper());
                    var indentation2 = indentation + "        ";
                    left.Dump("Left:", indentation2);
                    right?.Dump("Right:", indentation2); // this can be null on unary operators

                }
                else
                {
                    Console.WriteLine(indentation + "    Unary operator: " + op.ToUpper());
                    var indentation2 = indentation + "        ";
                    left.Dump("Expression:", indentation2);
                }
            }
        }

        [DebuggerDisplay("Function {name}({parameters.ToString()})")]
        class FunctionNode : SqlNode
        {
            public string name { get; private set; }
            public List<SqlNode> parameters { get; private set; }

            public FunctionNode(string functionName, List<SqlNode> args)
            {
                name = functionName;
                parameters = args;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                Console.WriteLine(indentation + "    Function - name: " + name +
                    ", number of arguments: " + parameters.Count);
                for (int i = 0; i < parameters.Count; i++)
                {
                    parameters[i].Dump("Arg " + i + ":", indentation + "        ");
                }
            }
        }

        /// <summary>Literal values</summary>
        [DebuggerDisplay("Lit. {value}")]
        class ValueNode : SqlNode
        {
            public dynamic value { get; private set; }
            public string dataType { get; private set; }

            public ValueNode(dynamic value, string type)
            {
                this.value = value;
                this.dataType = type;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                string literalValue = value == null ? "NULL" : value.ToString();
                Console.WriteLine(indentation + "    Literal " + dataType + ", value: " + literalValue);
            }
        }

        [DebuggerDisplay("Identifier {Value}")]
        class IdentifierNode : SqlNode
        {
            public enum Type
            {
                Table,
                Column
            }
            public string Value { get; private set; }

            public IdentifierNode(string value)
            {
                type = QueryParser.Type.Identifier;
                this.Value = value;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                Console.WriteLine(indentation + "    Identifier: " + Value);
            }
        }

        class RootNode : SqlNode
        {
            public List<SqlNode> Parameters { get; private set; }

            public RootNode(List<SqlNode> args)
            {
                Parameters = args;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                foreach (var param in Parameters)
                {
                    param.Dump(param.type.ToString(), indentation);
                    Console.WriteLine();
                }
            }
        }

        [DebuggerDisplay("Use {DbName}")]
        class UseNode : SqlNode
        {
            protected string DbName { get; private set; }

            public UseNode(string databaseName)
            {
                DbName = databaseName;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                Console.WriteLine(indentation + "Use: " + DbName);
            }
        }

        // Nodes for statements
        [DebuggerDisplay("Select {Select.ToString()} from {From}")]
        class SelectNode : SqlNode
        {
            public List<SqlNode> Select { get; private set; }
            public List<SqlNode> From { get; private set; }
            public SqlNode Where { get; private set; }
            public List<SqlNode> GroupBy { get; private set; }
            public SqlNode Having { get; private set; }
            public List<SqlNode> OrderBy { get; private set; }
            public bool Distinct { get; private set; }
            public SqlNode Limit { get; private set; }

            public SelectNode(List<SqlNode> select, List<SqlNode> from, SqlNode where,
                List<SqlNode> groupBy, SqlNode having, List<SqlNode> orderBy,
                bool distinct, SqlNode limit)
            {
                type = Type.SelectStatement;
                Select = select;
                From = from;
                Where = where;
                GroupBy = groupBy;
                Having = having;
                OrderBy = orderBy;
                Distinct = distinct;
                Limit = limit;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                var indentation2 = indentation + "    ";
                Select.DumpList("Select", indentation2);
                if (Distinct) Console.WriteLine(indentation2 + "Distinct");
                From.DumpList("From", indentation2);
                Where?.Dump("Where", indentation2);
                GroupBy?.DumpList("Group by", indentation2);
                Having?.Dump("Having", indentation2);
                OrderBy?.DumpList("Order by", indentation2);
                Limit?.Dump("Limit", indentation2);
            }
        }

        [DebuggerDisplay("Insert {table}")]
        class InsertNode : SqlNode
        {
            private readonly IdentifierNode table;
            private readonly List<SqlNode> columns;
            private readonly List<SqlNode> values;

            public InsertNode(IdentifierNode table, List<SqlNode> columns, List<SqlNode> values)
            {
                type = Type.InsertStatement;
                this.table = table;
                this.columns = columns;
                this.values = values;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                var indentation2 = indentation + "    ";
                columns.DumpList("Insert into: " + table.Value, indentation2);
                values.DumpList("Values", indentation2);
            }
        }

        [DebuggerDisplay("Delete {nameToRemove} when {condition}")]
        class DeleteNode : SqlNode
        {
            private readonly SqlNode nameToRemove;
            private readonly SqlNode condition;

            public DeleteNode(SqlNode nameToRemove, SqlNode condition)
            {
                type = Type.DeleteStatement;
                this.nameToRemove = nameToRemove;
                this.condition = condition;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump(itemName, indentation);
                nameToRemove.Dump("Name", indentation + "    ");
                condition.Dump("Condition", indentation + "    ");
            }
        }

        [DebuggerDisplay("Update {type}")]
        class UpdateNode : SqlNode
        {
            public UpdateNode()
            {
                type = Type.UpdateStatement;
            }

            public override void Dump(string itemName, string indentation)
            {
                base.Dump("Update", indentation);
            }
        }
        #endregion

        #region Syntax error handling
        class ParsingException : Exception
        {
            public ParsingException()
            {
            }

            public ParsingException(string message, long pos) :
                base("Error at offset " + pos + ": " + message)
            {
            }

            public ParsingException(string message, long pos, Exception inner) :
                base("Error at offset " + pos + ": " + message, inner)
            {
            }
        }

        private ParsingException ReportError(string error)
        {
            return new ParsingException(error, tokenizer.GetPos());
        }
        #endregion

        // ------------------------
        public QueryParser(Tokenizer tokenizer)
        {
            this.tokenizer = tokenizer;
            // tokenizer.Peek();
        }

        private delegate SqlNode Parser();

        public void Parse()
        {
            var statements = new List<SqlNode>();
            while (!tokenizer.IsEof)
            {
                var token = tokenizer.Next();
                if (token?.type == Token.Type.Keyword)
                    statements.Add(ParseStatement(token));
                else
                    throw ReportError("Expecting a statement. Got " + token.ToString());
            }
            rootNode = new RootNode(statements);
        }

        /// <summary>Dump the AST tree to screen</summary>
        public void Dump()
        {
            rootNode?.Dump("Sql Root Node:\n", "");
        }

        /// <summary>Check if a string is an operator</summary>
        public static bool IsOperator(string op)
        {
            return Precedence.ContainsKey(op);
        }
        public static bool IsWordOperator(string op)
        {
            return WordOperator.Contains(op);
        }

        /// <summary>Check if the next token is a keyword with the expected value</summary>
        /// <param name="keyword">The keyword to check</param>
        /// <returns>True if the next token is a keyword equal to the input argument</returns>
        public bool IsKeyword(string keyword)
        {
            var token = tokenizer.Peek();
            return token != null && token.IsKeyword(keyword);
        }

        public bool IsPunctuation(string punct)
        {
            var token = tokenizer.Peek();
            return token != null && token.IsPunctuation(punct);
        }

        public bool IsPunctuation(Token punct)
        {
            return tokenizer.Peek() == punct;
        }

        public void SkipPunctuation(string punct)
        {
            var token = tokenizer.Next();
            if (token == null || !token.IsPunctuation(punct))
                throw ReportError("Expecting '" + punct + "'. Got '" + token?.ToString() + "'");
        }

        public void Skip(Token punct)
        {
            var token = tokenizer.Next();
            if (punct != token)
                throw ReportError("Expecting punctuation '" + punct.value + "'. Got " + token?.ToString());
        }

        #region The main parsing function
        private SqlNode ParseStatement(Token statementName)
        {
            SqlNode retVal;
            switch (statementName.value.ToUpper())
            {
                case "SELECT":
                    retVal = ParseSelect();
                    break;
                case "UPDATE":
                    retVal = ParseUpdate();
                    break;
                case "INSERT":
                    retVal = ParseInsert();
                    break;
                case "DELETE":
                    retVal = ParseDelete();
                    break;
                case "USE":
                    retVal = ParseUse();
                    break;
                default:
                    throw ReportError("Invalid statement");
            }

            return retVal;
        }

        /// <summary>Parse delimited list of arguments, column list...</summary>
        /// <param name="start">The start token that starts the list. Pass null
        /// if the start token has already been swallowed</param>
        /// <param name="consumeStop">If true, skip over the stop token</param>
        private List<SqlNode> ParseDelimitedList(Token start, Token stop, Token separator,
            Parser parseItem, bool consumeStop = true)
        {
            // If start is null then the start delimiter has been consumed and we won't check it
            if (start != null)
            {
                var startToken = tokenizer.Peek();
                if (startToken != start)
                    throw ReportError("Expecting " + start.value + " token. Got " + startToken.ToString());
                tokenizer.Next();
            }

            var argumentList = new List<SqlNode>();
            bool isFirstToken = true;
            while (!tokenizer.IsEof)
            {
                // Exit when we've came to the stop token
                if (tokenizer.Peek() == stop)
                    break;
                if (isFirstToken)
                {
                    isFirstToken = false;
                }
                else
                {
                    if (tokenizer.Peek() == stop)
                        break;
                    Skip(separator);
                }

                var arg = parseItem();
                argumentList.Add(arg);
            }
            if (consumeStop)
                Skip(stop);
            return argumentList;
        }

        /// <summary>Parse delimited list of arguments, column list...</summary>
        private List<SqlNode> ParseDelimitedList(Token start, List<Token> stop,
            out Token stopToken, Token separator, Parser parseItem, bool consumeStop = false)
        {
            // If start is null then the start delimiter has been consumed and we won't check it
            if (start != null && IsPunctuation(start))
                throw ReportError("Expecting " + start.value + " token. Got " + tokenizer.Next().ToString());

            var argumentList = new List<SqlNode>();
            bool isFirstToken = true;
            while (!tokenizer.IsEof)
            {
                // Exit when we've came to the stop token
                var token = tokenizer.Peek();
                if (stop.Contains(token))
                    break;
                if (isFirstToken)
                {
                    isFirstToken = false;
                }
                else
                {
                    Skip(separator);
                }
                if (stop.Contains(token))
                    break;
                argumentList.Add(parseItem());
            }
            if (consumeStop)
                stopToken = tokenizer.Next();
            else
                stopToken = tokenizer.Peek();
            return argumentList;
        }

        private SqlNode ParseFunction(SqlNode func)
        {
            if (func is IdentifierNode funcName)
            {
                var argList = ParseDelimitedList(LeftParen, RightParen, Comma, ParseExpression);
                return new FunctionNode(funcName.Value, argList);
            }
            else
            {
                throw ReportError("Expecting a function name. Got " + func.ToString());
            }
        }

        private SqlNode ParseRightSideExpression(SqlNode atom)
        {
            return ParseBinary(atom, 0);
        }
        private SqlNode ParseExpression()
        {
            var atom = ParseAtom();
            return ParseFunctionOrSingleNode(() => ParseRightSideExpression(atom));
        }

        private IdentifierNode ParseIdendifier()
        {
            var token = tokenizer.Next();
            if (token?.type == Token.Type.Identifier)
                return new IdentifierNode(token.value);
            else
                throw ReportError("Expecting an identifier. Got " + token.ToString());
        }

        private SqlNode ParseLiteral(Token token)
        {
            switch (token.type)
            {
                case Token.Type.Integer:
                    return new ValueNode(long.Parse(token.value), "long");
                case Token.Type.Float:
                    return new ValueNode(double.Parse(token.value), "double");
                case Token.Type.String:
                    return new ValueNode(token.value, "string");
                case Token.Type.Boolean:
                    return new ValueNode(token.value.EqualsIgnoreCase("TRUE"), "bool");
                case Token.Type.Keyword:
                    if (token.value.EqualsIgnoreCase("NULL"))
                        return new ValueNode(null, "null");
                    else
                        goto default;
                default:
                    throw ReportError("Unexpected token " + token.ToString());
            }
        }

        private SqlNode ParseToken()
        {
            var token = tokenizer.Peek();
            // Check for unary operators
            Token unaryOperator = null;
            if (token.type == Token.Type.Operator && (token.value == "-" || token.value == "+"))
            {
                unaryOperator = tokenizer.Next();
                token = tokenizer.Peek();
            }

            if (token.IsPunctuation("("))
            {
                tokenizer.Next();   // skip "("
                var parser = ParseExpression();
                SkipPunctuation(")");
                if (unaryOperator != null)
                    return new ExpressionNode(parser, unaryOperator.value);
                return parser;
            }

            token = tokenizer.Next();
            if (token.type == Token.Type.Identifier)
            {
                var identifier = new IdentifierNode(token.value);
                if (unaryOperator != null)
                    return new ExpressionNode(identifier, unaryOperator.value);
                return identifier;
            }

            var literal = ParseLiteral(token);
            if (unaryOperator != null)
                return new ExpressionNode(literal, unaryOperator.value);
            return literal;
        }

        private SqlNode ParseAtom()
        {
            return ParseFunctionOrSingleNode(ParseToken);
        }

        private List<SqlNode> ParseSelectList()
        {
            var token = tokenizer.Peek();
            if (token.type == Token.Type.Operator && token.value == "*")
            {
                // SELECT *
                tokenizer.Next();
                return new List<SqlNode> { new IdentifierNode("*") };
            }
            else if (token.IsIdentifier("COUNT"))
            {
                // SELECT COUNT(*), SELECT COUNT(ALL/DISTINCT...)
                var lPar = tokenizer.SkipAndPeek();
                if (!lPar.IsPunctuation("("))
                    ReportError("Expecting '(', got " + lPar.ToString());
                var filter = tokenizer.Skip();
                List<SqlNode> result;
                if (filter.IsOperator("*"))
                {
                    result = new List<SqlNode> {
                        new FunctionNode("COUNT", new List<SqlNode> { new IdentifierNode("*") })
                    };
                }
                else if (filter.IsKeyword("ALL") || filter.IsKeyword("DISTINCT"))
                {
                    var columnName = tokenizer.Next();
                    result = new List<SqlNode> {
                        new ExpressionNode(new IdentifierNode(columnName.value), filter.value, null)
                    };
                }
                else
                {
                    result = new List<SqlNode> { new IdentifierNode(filter.value) };
                }
                SkipPunctuation(")");
                return result;
            }
            else
            {
                return ParseDelimitedList(null, From, Comma, ParseExpression, consumeStop: false);
            }
        }

        private SqlNode ParseOrderList()
        {
            var token = tokenizer.Peek();
            if (token.type != Token.Type.Identifier)
                throw ReportError("Expecting an identifier. Got " + token.ToString());

            var identifier = new IdentifierNode(token.value);
            var order = tokenizer.SkipAndPeek();
            if (order.IsKeyword("ASC") || order.IsKeyword("DESC"))
            {
                tokenizer.Next();
                return new ExpressionNode(identifier, order.value);
            }
            else
            {
                return identifier;
            }
        }

        // Statement parsers
        private SqlNode ParseSelect()
        {
            if (tokenizer.IsEof)
                throw ReportError("Unexpected EOF");

            bool distinct = false;
            var token = tokenizer.Peek();
            if (token.IsKeyword("DISTINCT"))
                distinct = true;

            var select = ParseSelectList();

            List<SqlNode> fromNode = null;
            Token nextToken = null;
            if (IsKeyword("FROM"))
            {
                tokenizer.Next();
                fromNode = ParseDelimitedList(null, FromStopCondition, out nextToken, Comma, ParseIdendifier);
            }
            else
            {
                ReportError("Expecting FROM, got " + tokenizer.Peek().value);
            }

            bool statementEnded = false;
            if (IsPunctuation(";"))
                statementEnded = true;

            SqlNode whereNode = null;
            if (!statementEnded && IsKeyword("WHERE"))
            {
                tokenizer.Next();
                whereNode = ParseExpression();
            }

            List<SqlNode> groupByNode = null;
            if (IsPunctuation(";"))
                statementEnded = true;
            if (!statementEnded && IsKeyword("GROUP"))
            {
                var byKeyword = tokenizer.SkipAndPeek();
                if (!byKeyword.IsKeyword("BY"))
                {
                    throw ReportError("Expecting BY keyword. Got " + byKeyword.ToString());
                }
                tokenizer.Next();
                groupByNode = ParseDelimitedList(null, GroupByStopCondition,
                    out Token stopToken, Comma, ParseExpression);
            }

            SqlNode havingNode = null;
            if (!statementEnded && IsKeyword("HAVING"))
            {
                tokenizer.Next();
                havingNode = ParseExpression();
            }

            List<SqlNode> orderByNode = null;
            if (!statementEnded && IsKeyword("ORDER"))
            {
                var byToken = tokenizer.SkipAndPeek();
                if (!byToken.IsKeyword("BY"))
                {
                    throw ReportError("Expecting BY keyword. Got " + byToken.ToString());
                }
                tokenizer.Next();
                Token stopToken;
                orderByNode = ParseDelimitedList(null, OrderByStopCondition, out stopToken, Comma, ParseOrderList);
                if (stopToken.type == Token.Type.Keyword &&
                    (stopToken.value.EqualsIgnoreCase("ASC") || stopToken.value.EqualsIgnoreCase("DESC")))
                    tokenizer.Next();   // not supported asc/desc yet
            }

            if (IsPunctuation(";"))
            {
                SkipPunctuation(";");
            }
            return new SelectNode(select, fromNode, whereNode, groupByNode,
                havingNode, orderByNode, distinct, null);
        }

        private SqlNode ParseUpdate()
        {
            SkipPunctuation(";");
            return null;
        }

        private SqlNode ParseInsert()
        {
            if (tokenizer.IsEof)
                throw ReportError("Unexpected EOF");

            var token = tokenizer.Peek();
            if (!token.IsKeyword("INTO"))
                throw ReportError("Expecting INTO keyword. Got " + token.ToString());

            tokenizer.Next();
            var tableName = ParseIdendifier();
            List<SqlNode> columnList = null;
            var lPar = tokenizer.Peek();
            if (lPar.IsPunctuation("("))
            {
                columnList = ParseDelimitedList(LeftParen, RightParen, Comma, ParseExpression);
            }

            List<SqlNode> valueList = null;
            if (IsKeyword("VALUES"))
            {
                tokenizer.Next();
                valueList = ParseDelimitedList(LeftParen, RightParen, Comma, ParseExpression);
            }

            SkipPunctuation(";");
            return new InsertNode(tableName, columnList, valueList);
        }

        private SqlNode ParseDelete()
        {
            if (tokenizer.IsEof)
                throw ReportError("Unexpected EOF");

            var token = tokenizer.Peek();
            if (!token.IsKeyword("FROM"))
                throw ReportError("Expecting FROM keyword. Got " + token.ToString());

            tokenizer.Next();
            SqlNode nameToRemove = ParseIdendifier();

            SqlNode condition = null;
            if (IsKeyword("WHERE"))
            {
                tokenizer.Next();
                condition = ParseExpression();
            }

            SkipPunctuation(";");
            return new DeleteNode(nameToRemove, condition);
        }

        private SqlNode ParseUse()
        {
            if (tokenizer.IsEof)
                throw ReportError("Unexpected EOF");

            var dbName = ParseIdendifier();

            SkipPunctuation(";");
            return new UseNode(dbName.Value);
        }
        #endregion

        private SqlNode ParseBinary(SqlNode lhs, int lhsPrecedence)
        {
            var token = tokenizer.Peek();

            if (token?.type == Token.Type.Operator)
            {
                var rhsPrecedence = Precedence[token.value];
                if (lhsPrecedence < rhsPrecedence)
                {
                    tokenizer.Next();
                    var atom = ParseAtom();
                    var rhs = ParseBinary(atom, rhsPrecedence);
                    var binaryNode = new ExpressionNode(lhs, token.value, rhs);
                    return ParseBinary(binaryNode, lhsPrecedence);
                }
            }
            return lhs;
        }

        private SqlNode ParseFunctionOrSingleNode(Parser parse)
        {
            SqlNode nextNode = parse();
            // return IsPunctuation("(") ? ParseFunction(nextNode) : nextNode;
            if (IsPunctuation("("))
                return ParseFunction(nextNode);
            else
                return nextNode;
        }
    }
}
