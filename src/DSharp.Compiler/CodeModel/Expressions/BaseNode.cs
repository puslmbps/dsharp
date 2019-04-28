// BaseNode.cs
// Script#/Core/Compiler
// This source code is subject to terms and conditions of the Apache License, Version 2.0.
//

using DSharp.Compiler.CodeModel.Tokens;

namespace DSharp.Compiler.CodeModel.Expressions
{
    internal sealed class BaseNode : ExpressionNode
    {
        public BaseNode(Token token)
            : base(ParseNodeType.Base, token)
        {
        }
    }
}