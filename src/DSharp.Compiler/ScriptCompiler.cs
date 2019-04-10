﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DSharp.Compiler.CodeModel;
using DSharp.Compiler.CodeModel.Types;
using DSharp.Compiler.Compiler;
using DSharp.Compiler.Errors;
using DSharp.Compiler.Generator;
using DSharp.Compiler.Importer;
using DSharp.Compiler.Metadata;
using DSharp.Compiler.ScriptModel;
using DSharp.Compiler.ScriptModel.Symbols;
using DSharp.Compiler.Validator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DSharp.Compiler
{
    public sealed class ScriptCompiler
    {
        private readonly IErrorHandler errorHandler;

        private ICollection<TypeSymbol> appSymbols;
        private ParseNodeList compilationUnitList;
        private CompilerOptions options;
        private IScriptModel scriptModel;

        private ScriptMetadata ScriptMetadata => scriptModel.ScriptMetadata;

        private bool HasErrors => errorHandler.HasErrors;

        public ScriptCompiler()
            : this(null)
        {
        }

        public ScriptCompiler(IErrorHandler errorHandler)
        {
            this.errorHandler = errorHandler ?? new ConsoleLoggingErrorHandler();
        }

        public bool Compile(CompilerOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));

            scriptModel = new ES5ScriptModel();

            var compilation = ImportMetadata();

            if (HasErrors)
            {
                return false;
            }

            compilation = BuildCodeModel(compilation);

            if (HasErrors)
            {
                return false;
            }

            BuildMetadata();

            if (HasErrors)
            {
                return false;
            }

            BuildImplementation();

            if (HasErrors)
            {
                return false;
            }

            GenerateScript();

            if (HasErrors)
            {
                return false;
            }

            return true;
        }

        private CSharpCompilation ImportMetadata()
        {
            var references = options.References.Select(r => MetadataReference.CreateFromFile(r));
            var compilationContext = CSharpCompilation.Create(options.AssemblyName)
                .AddReferences(references);

            MetadataImporter mdImporter = new MetadataImporter(errorHandler);

            mdImporter.ImportMetadata(options.References, scriptModel);
            return compilationContext;
        }

        private CSharpCompilation BuildCodeModel(CSharpCompilation compilation)
        {
            compilationUnitList = new ParseNodeList();

            CodeModelBuilder codeModelBuilder = new CodeModelBuilder(options, errorHandler);
            CodeModelValidator codeModelValidator = new CodeModelValidator(errorHandler);
            CodeModelProcessor validationProcessor = new CodeModelProcessor(codeModelValidator, options);

            foreach (IStreamSource source in options.Sources)
            {

                try
                {
                    CompilationUnitNode compilationUnit = codeModelBuilder.BuildCodeModel(source);

                    if (compilationUnit != null)
                    {
                        validationProcessor.Process(compilationUnit);

                        compilationUnitList.Append(compilationUnit);
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Error occurred in File {source?.FullName}: {e.Message}, {e.StackTrace}  ");
                }
            }

            CSharpParseOptions cSharpParseOptions = new CSharpParseOptions(LanguageVersion.CSharp2)
                .WithPreprocessorSymbols(options.Defines)
                .WithKind(SourceCodeKind.Regular);

            foreach (var tree in options.Sources.Select(source =>
            {
                return CSharpSyntaxTree.ParseText(SourceText.From(source.GetStream()), cSharpParseOptions).GetRoot();
            }).ToList())
            {
                compilation = compilation.AddSyntaxTrees(tree.SyntaxTree);
            }

            return compilation;
        }

        private void BuildMetadata()
        {
            if (options.Resources != null && options.Resources.Count != 0)
            {
                ResourcesBuilder resourcesBuilder = new ResourcesBuilder(scriptModel.Resources);
                resourcesBuilder.BuildResources(options.Resources);
            }

            IScriptModelBuilder<ParseNodeList> mdBuilder = new MonoLegacyMetadataBuilder();
            IScriptMetadataBuilder<ParseNodeList> scriptMetadataBuilder = new MonoLegacyScriptMetadataBuilder(errorHandler);

            scriptModel.ScriptMetadata = scriptMetadataBuilder.Build(compilationUnitList, scriptModel, options);

            appSymbols = mdBuilder.BuildMetadata(compilationUnitList, scriptModel, options);

            // Check if any of the types defined in this assembly conflict.
            Dictionary<string, TypeSymbol> types = new Dictionary<string, TypeSymbol>();

            foreach (TypeSymbol appType in appSymbols)
            {
                if (appType.IsApplicationType == false || appType.Type == SymbolType.Delegate)
                {
                    // Skip the check for types that are marked as imported, as they
                    // aren't going to be generated into the script.
                    // Delegates are implicitly imported types, as they're never generated into
                    // the script.
                    continue;
                }

                if (appType.Type == SymbolType.Class &&
                    ((ClassSymbol)appType).PrimaryPartialClass != appType)
                {
                    // Skip the check for partial types, since they should only be
                    // checked once.
                    continue;
                }

                // TODO: We could allow conflicting types as long as both aren't public
                //       since they won't be on the exported types list. Internal types that
                //       conflict could be generated using full name.

                string name = appType.GeneratedName;

                if (types.ContainsKey(name))
                {
                    errorHandler.ReportGeneralError(string.Format(DSharpStringResources.CONFLICTING_TYPE_NAME_ERROR_FORMAT, appType.FullName, types[name].FullName));
                }
                else
                {
                    types[name] = appType;
                }
            }

            ISymbolTransformer transformer = null;

            if (options.Minimize)
            {
                transformer = new SymbolObfuscator();
            }
            else
            {
                transformer = new SymbolInternalizer();
            }

            if (transformer != null)
            {
                SymbolSetTransformer symbolSetTransformer = new SymbolSetTransformer(transformer);
                ICollection<Symbol> transformedSymbols =
                    symbolSetTransformer.TransformSymbolSet(scriptModel, useInheritanceOrder: true);
            }
        }

        private void BuildImplementation()
        {
            CodeBuilder codeBuilder = new CodeBuilder(options, errorHandler);
            ICollection<SymbolImplementation> implementations = codeBuilder.BuildCode(scriptModel);

            if (options.Minimize)
            {
                foreach (SymbolImplementation impl in implementations)
                {
                    if (impl.Scope == null)
                    {
                        continue;
                    }

                    SymbolObfuscator obfuscator = new SymbolObfuscator();
                    SymbolImplementationTransformer transformer = new SymbolImplementationTransformer(obfuscator);

                    transformer.TransformSymbolImplementation(impl);
                }
            }
        }

        private void GenerateScript()
        {
            Stream outputStream = null;
            TextWriter outputWriter = null;

            using (outputStream = options.ScriptFile.GetStream())
            {
                if (outputStream == null)
                {
                    string scriptName = options.ScriptFile.FullName;
                    errorHandler.ReportMissingStreamError(scriptName);

                    return;
                }

                outputWriter = new StreamWriter(outputStream, new UTF8Encoding(false));

                string script = GenerateScriptWithTemplate();
                outputWriter.Write(script);
            }
        }

        private string GenerateScriptCore()
        {
            StringWriter scriptWriter = new StringWriter();

            try
            {
                ScriptGenerator scriptGenerator = new ScriptGenerator(scriptWriter, scriptModel.ScriptMetadata);
                scriptGenerator.GenerateScript(scriptModel);
            }
            catch (Exception e)
            {
                Debug.Fail(e.ToString());
            }
            finally
            {
                scriptWriter.Flush();
            }

            return scriptWriter.ToString();
        }

        private string GenerateScriptWithTemplate()
        {
            string script = GenerateScriptCore();

            string template = ScriptMetadata.Template;

            if (string.IsNullOrEmpty(template))
            {
                return script;
            }

            template = PreprocessTemplate(template);

            StringBuilder requiresBuilder = new StringBuilder();
            StringBuilder dependenciesBuilder = new StringBuilder();
            StringBuilder depLookupBuilder = new StringBuilder();

            bool firstDependency = true;

            foreach (ScriptReference dependency in scriptModel.Dependencies)
            {
                if (dependency.DelayLoaded)
                {
                    continue;
                }

                if (firstDependency)
                {
                    depLookupBuilder.Append("var ");
                }
                else
                {
                    requiresBuilder.Append(", ");
                    dependenciesBuilder.Append(", ");
                    depLookupBuilder.Append(",\r\n    ");
                }

                string name = dependency.Name;

                if (name == DSharpStringResources.DSHARP_SCRIPT_NAME)
                {
                    // TODO: This is a hack... to make generated node.js scripts
                    //       be able to reference the 'dsharp' node module.
                    //       Fix this in a better/1st class manner by allowing
                    //       script assemblies to declare such things.
                    name = DSharpStringResources.DSHARP_SCRIPT_NAME;
                }

                requiresBuilder.Append("'" + dependency.Path + "'");
                dependenciesBuilder.Append(dependency.Identifier);

                depLookupBuilder.Append(dependency.Identifier);
                depLookupBuilder.Append(" = require('" + name + "')");

                firstDependency = false;
            }

            depLookupBuilder.Append(";");

            return template.TrimStart()
                           .Replace("{name}", scriptModel.ScriptMetadata.ScriptName)
                           .Replace("{description}", ScriptMetadata.Description ?? string.Empty)
                           .Replace("{copyright}", ScriptMetadata.Copyright ?? string.Empty)
                           .Replace("{version}", ScriptMetadata.Version ?? string.Empty)
                           .Replace("{compiler}", typeof(ScriptCompiler).Assembly.GetName().Version.ToString())
                           .Replace("{description}", ScriptMetadata.Description)
                           .Replace("{requires}", requiresBuilder.ToString())
                           .Replace("{dependencies}", dependenciesBuilder.ToString())
                           .Replace("{dependenciesLookup}", depLookupBuilder.ToString())
                           .Replace("{script}", script);
        }

        private string PreprocessTemplate(string template)
        {
            if (options.IncludeResolver == null)
            {
                return template;
            }

            Regex includePattern = new Regex("\\{include:([^\\}]+)\\}",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);

            return includePattern.Replace(template, delegate (Match include)
            {
                string includedScript = string.Empty;

                if (include.Groups.Count == 2)
                {
                    string includePath = include.Groups[1].Value;

                    IStreamSource includeSource = options.IncludeResolver.Resolve(includePath);

                    if (includeSource != null)
                    {
                        using (Stream includeStream = includeSource.GetStream())
                        {
                            StreamReader reader = new StreamReader(includeStream);

                            return reader.ReadToEnd();
                        }
                    }
                }

                return includedScript;
            });
        }
    }

    public class RoslynMetadataImporter
    {
        public CSharpCompilation ImportMetadata(CSharpCompilation compilation, IEnumerable<string> references)
        {
            var metadataReferences = references.Select(r => MetadataReference.CreateFromFile(r));

            compilation = compilation.WithReferences(metadataReferences);

            foreach (var reference in metadataReferences)
            {
                var metadata = reference.GetMetadata() as AssemblyMetadata;
                foreach (var module in metadata.GetModules())
                {
                    var metadataReader = module.GetMetadataReader();
                    foreach (var typeDefinition in metadataReader.TypeDefinitions.Select(def => metadataReader.GetTypeDefinition(def)))
                    {
                        var ns = metadataReader.GetNamespaceDefinition(typeDefinition.NamespaceDefinition);
                        Console.WriteLine($"Reading Type: {metadataReader.GetString(typeDefinition.Name)}, Namespace: {metadataReader.GetString(ns.Name)}, Module: {module.Name}");
                    }
                }
            }

            return compilation;
        }
    }

    public class ConsoleLoggingErrorHandler : IErrorHandler
    {
        public bool HasErrors { get; private set; }

        public void ReportError(CompilerError error)
        {
            HasErrors = true;

            if (error.ColumnNumber != null || error.LineNumber != null)
            {
                Console.Error.WriteLine($"{error.File}({error.LineNumber.GetValueOrDefault()},{error.ColumnNumber.GetValueOrDefault()})");
            }

            Console.Error.WriteLine(error.Description);
        }
    }
}