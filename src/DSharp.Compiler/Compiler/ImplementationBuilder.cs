﻿// ImplementationBuilder.cs
// Script#/Core/Compiler
// This source code is subject to terms and conditions of the Apache License, Version 2.0.
//

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using DSharp.Compiler.CodeModel.Expressions;
using DSharp.Compiler.CodeModel.Members;
using DSharp.Compiler.CodeModel.Statements;
using DSharp.Compiler.Errors;
using DSharp.Compiler.ScriptModel.Expressions;
using DSharp.Compiler.ScriptModel.Statements;
using DSharp.Compiler.ScriptModel.Symbols;

namespace DSharp.Compiler.Compiler
{
    internal sealed class ImplementationBuilder : ILocalSymbolTable
    {
        private readonly IErrorHandler errorHandler;

        private readonly CompilerOptions options;
        private SymbolScope currentScope;

        private int generatedSymbolCount;

        private SymbolScope rootScope;

        public ImplementationBuilder(CompilerOptions options, IErrorHandler errorHandler)
        {
            this.options = options;
            this.errorHandler = errorHandler;
        }

        private SymbolImplementation BuildImplementation(IScriptSymbolTable symbolTable, CodeMemberSymbol symbolContext,
                                                         BlockStatementNode implementationNode, bool addAllParameters)
        {
            rootScope = new SymbolScope(symbolTable);
            currentScope = rootScope;

            List<Statement> statements = new List<Statement>();
            StatementBuilder statementBuilder = new StatementBuilder(this, symbolContext, errorHandler, options);

            if (symbolContext.Parameters != null)
            {
                int parameterCount = symbolContext.Parameters.Count;

                if (addAllParameters == false)
                {
                    // For property getters (including indexers), we don't add the last parameter,
                    // which happens to be the "value" parameter, which only makes sense
                    // for the setter.

                    parameterCount--;
                }

                for (int paramIndex = 0; paramIndex < parameterCount; paramIndex++)
                    currentScope.AddSymbol(symbolContext.Parameters[paramIndex]);
            }

            if (symbolContext.Type == SymbolType.Constructor &&
                (((ConstructorSymbol) symbolContext).Visibility & MemberVisibility.Static) == 0)
            {
                Debug.Assert(symbolContext.Parent is ClassSymbol);

                if (((ClassSymbol) symbolContext.Parent).BaseClass != null)
                {
                    BaseInitializerExpression baseExpr = new BaseInitializerExpression();

                    ConstructorDeclarationNode ctorNode = (ConstructorDeclarationNode) symbolContext.ParseContext;

                    if (ctorNode.BaseArguments != null)
                    {
                        ExpressionBuilder expressionBuilder =
                            new ExpressionBuilder(this, symbolContext, errorHandler, options);

                        Debug.Assert(ctorNode.BaseArguments is ExpressionListNode);
                        ICollection<Expression> args =
                            expressionBuilder.BuildExpressionList((ExpressionListNode) ctorNode.BaseArguments);

                        foreach (Expression paramExpr in args) baseExpr.AddParameterValue(paramExpr);
                    }

                    statements.Add(new ExpressionStatement(baseExpr));
                }
            }

            foreach (StatementNode statementNode in implementationNode.Statements)
            {
                Statement statement = statementBuilder.BuildStatement(statementNode);

                if (statement != null)
                {
                    statements.Add(statement);
                }
            }

            string thisIdentifier = "this";

            if (symbolContext.Type == SymbolType.AnonymousMethod)
            {
                thisIdentifier = "$this";
            }

            return new SymbolImplementation(statements, rootScope, thisIdentifier);
        }

        public SymbolImplementation BuildEventAdd(EventSymbol eventSymbol)
        {
            AccessorNode addNode = ((EventDeclarationNode) eventSymbol.ParseContext).Property.SetAccessor;
            BlockStatementNode accessorBody = addNode.Implementation;

            return BuildImplementation((IScriptSymbolTable) eventSymbol.Parent,
                eventSymbol, accessorBody, /* addParameters */ true);
        }

        public SymbolImplementation BuildEventRemove(EventSymbol eventSymbol)
        {
            AccessorNode removeNode = ((EventDeclarationNode) eventSymbol.ParseContext).Property.GetAccessor;
            BlockStatementNode accessorBody = removeNode.Implementation;

            return BuildImplementation((IScriptSymbolTable) eventSymbol.Parent,
                eventSymbol, accessorBody, /* addParameters */ true);
        }

        public SymbolImplementation BuildField(FieldSymbol fieldSymbol)
        {
            rootScope = new SymbolScope((IScriptSymbolTable) fieldSymbol.Parent);
            currentScope = rootScope;

            Expression initializerExpression = null;

            FieldDeclarationNode fieldDeclarationNode = (FieldDeclarationNode) fieldSymbol.ParseContext;
            Debug.Assert(fieldDeclarationNode != null);

            VariableInitializerNode initializerNode = (VariableInitializerNode) fieldDeclarationNode.Initializers[0];

            if (initializerNode.Value != null)
            {
                ExpressionBuilder expressionBuilder = new ExpressionBuilder(this, fieldSymbol, errorHandler, options);
                initializerExpression = expressionBuilder.BuildExpression(initializerNode.Value);

                if (initializerExpression is MemberExpression)
                {
                    initializerExpression =
                        expressionBuilder.TransformMemberExpression((MemberExpression) initializerExpression);
                }
            }
            else
            {
                object defaultValue = null;

                ITypeSymbol fieldType = fieldSymbol.AssociatedType;
                var scriptModel = fieldSymbol.Root;
                var symbolResolver = scriptModel.SymbolResolver;

                if (fieldType.Type == SymbolType.Enumeration)
                {
                    // The default for named values is null, so this only applies to
                    // regular enum types

                    EnumerationSymbol enumType = (EnumerationSymbol) fieldType;

                    if (enumType.UseNamedValues == false)
                    {
                        defaultValue = 0;
                    }
                }
                else if (fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Integer) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.UnsignedInteger) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Long) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.UnsignedLong) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Short) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.UnsignedShort) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Byte) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.SignedByte) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Double) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Single) ||
                         fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Decimal))
                {
                    defaultValue = 0;
                }
                else if (fieldType == symbolResolver.ResolveIntrinsicType(IntrinsicType.Boolean))
                {
                    defaultValue = false;
                }

                if (defaultValue != null)
                {
                    initializerExpression =
                        new LiteralExpression(symbolResolver.ResolveIntrinsicType(IntrinsicType.Object),
                            defaultValue);
                    fieldSymbol.SetImplementationState( /* hasInitializer */ true);
                }
            }

            if (initializerExpression != null)
            {
                List<Statement> statements = new List<Statement>();
                statements.Add(new ExpressionStatement(initializerExpression, /* isFragment */ true));

                return new SymbolImplementation(statements, null, "this");
            }

            return null;
        }

        public SymbolImplementation BuildMethod(MethodSymbol methodSymbol)
        {
            BlockStatementNode methodBody = ((MethodDeclarationNode) methodSymbol.ParseContext).Implementation;

            return BuildImplementation((IScriptSymbolTable) methodSymbol.Parent,
                methodSymbol, methodBody, /* addAllParameters */ true);
        }

        public SymbolImplementation BuildMethod(AnonymousMethodSymbol methodSymbol)
        {
            BlockStatementNode methodBody = ((AnonymousMethodNode) methodSymbol.ParseContext).Implementation;

            return BuildImplementation(methodSymbol.StackContext,
                methodSymbol, methodBody, /* addAllParameters */ true);
        }

        public SymbolImplementation BuildIndexerGetter(IndexerSymbol indexerSymbol)
        {
            AccessorNode getterNode = ((IndexerDeclarationNode) indexerSymbol.ParseContext).GetAccessor;
            BlockStatementNode accessorBody = getterNode.Implementation;

            return BuildImplementation((IScriptSymbolTable) indexerSymbol.Parent,
                indexerSymbol, accessorBody, /* addAllParameters */ false);
        }

        public SymbolImplementation BuildIndexerSetter(IndexerSymbol indexerSymbol)
        {
            AccessorNode setterNode = ((IndexerDeclarationNode) indexerSymbol.ParseContext).SetAccessor;
            BlockStatementNode accessorBody = setterNode.Implementation;

            return BuildImplementation((IScriptSymbolTable) indexerSymbol.Parent,
                indexerSymbol, accessorBody, /* addAllParameters */ true);
        }

        public SymbolImplementation BuildPropertyGetter(PropertySymbol propertySymbol)
        {
            AccessorNode getterNode = ((PropertyDeclarationNode) propertySymbol.ParseContext).GetAccessor;

            if (getterNode == null)
            {
                return null;
            }

            BlockStatementNode accessorBody = getterNode.Implementation;

            return BuildImplementation((IScriptSymbolTable) propertySymbol.Parent,
                propertySymbol, accessorBody, /* addAllParameters */ false);
        }

        public SymbolImplementation BuildPropertySetter(PropertySymbol propertySymbol)
        {
            AccessorNode setterNode = ((PropertyDeclarationNode) propertySymbol.ParseContext).SetAccessor;

            if (setterNode == null)
            {
                return null;
            }

            BlockStatementNode accessorBody = setterNode.Implementation;

            return BuildImplementation((IScriptSymbolTable) propertySymbol.Parent,
                propertySymbol, accessorBody, /* addAllParameters */ true);
        }

        public IEnumerable<ISymbol> Symbols
        {
            get
            {
                Debug.Assert(currentScope != null);

                return ((IScriptSymbolTable) currentScope).Symbols;
            }
        }

        public ISymbol FindSymbol(string name, ISymbol context, SymbolFilter filter)
        {
            Debug.Assert(currentScope != null);

            return ((IScriptSymbolTable) currentScope).FindSymbol(name, context, filter);
        }

        void ILocalSymbolTable.AddSymbol(LocalSymbol symbol)
        {
            Debug.Assert(currentScope != null);
            currentScope.AddSymbol(symbol);
        }

        string ILocalSymbolTable.CreateSymbolName(string nameHint)
        {
            generatedSymbolCount++;

            return "$" + nameHint + generatedSymbolCount;
        }

        void ILocalSymbolTable.PopScope()
        {
            Debug.Assert(currentScope != null);
            currentScope = currentScope.Parent;
        }

        void ILocalSymbolTable.PushScope()
        {
            Debug.Assert(currentScope != null);

            SymbolScope parentScope = currentScope;

            currentScope = new SymbolScope(parentScope);
            parentScope.AddChildScope(currentScope);
        }
    }
}