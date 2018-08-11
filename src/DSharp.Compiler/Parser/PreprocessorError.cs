// PreprocessorError.cs
// Script#/Core/Compiler
// This source code is subject to terms and conditions of the Apache License, Version 2.0.
//

namespace DSharp.Compiler.Parser
{
    internal sealed class PreprocessorError
    {
        public static readonly Error UnexpectedEndOfFile =
            new Error(200, 0, "Unexpected end of file found in preprocessor directive");

        public static readonly Error TokenExpected =
            new Error(201, 0, "Syntax error in preprocessor directive: '{0}' expected");

        public static readonly Error PpAfterSource = new Error(202, 0,
            "Preprocessing directive must be first non-whitespace character on a line");

        public static readonly Error PpError = new Error(203, 0, "#error: '{0}'");
        public static readonly Error PpWarning = new Error(204, 1, "#warning: '{0}'");
        public static readonly Error UnexpectedDirective = new Error(205, 0, "Unexpected pre-processor directive");
        public static readonly Error MisingPpExpression = new Error(206, 0, "Missing pre-processor expression");
        public static readonly Error EndRegionExpected = new Error(207, 0, "#endregion expected");

        public static readonly Error DefineAfterToken =
            new Error(208, 0, "#define and #undef must occur before any other tokens in a file");

        public static readonly Error PpEndifExpected = new Error(209, 0, "#endif expected");

        private PreprocessorError()
        {
        }
    }
}