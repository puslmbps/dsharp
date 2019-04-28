﻿// ResXCodeBuilder.cs
// Script#/Core/Build
// This source code is subject to terms and conditions of the Apache License, Version 2.0.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DSharp.Build.Generators
{

    public sealed class ResXCodeBuilder
    {
        private static readonly string ResourcesHeader =
@"//------------------------------------------------------------------------------
// <auto-generated>
// Resources.g.cs
// Do not edit directly. This file has been auto-generated from .resx resources.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace {0} {{
";

        private static readonly string ResourcesFooter =
@"}";

        private StringBuilder codeBuilder;

        public void Start(string namespaceName)
        {
            codeBuilder = new StringBuilder();
            codeBuilder.AppendFormat(ResourcesHeader, namespaceName);
        }

        public string End()
        {
            codeBuilder.AppendLine(ResourcesFooter);
            return codeBuilder.ToString();
        }

        public void GenerateCode(string resourceFileName, string resourceFileContent, string resourceGenerator)
        {
            if (IsLocalizedResourceFile(resourceFileName))
            {
                return;
            }

            List<ResXItem> resourceItems = ResXParser.ParseResxMarkup(resourceFileContent);
            if (resourceItems.Count == 0)
            {
                return;
            }

            string className = Path.GetFileNameWithoutExtension(resourceFileName);
            string accessModifier = "internal";
            if (string.Compare(resourceGenerator, "PublicResxScriptGenerator", StringComparison.OrdinalIgnoreCase) == 0)
            {
                accessModifier = "public";
            }

            codeBuilder.AppendLine();
            codeBuilder.AppendFormat("    /// <summary>{0} resources class</summary>", className);
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("    [ScriptResources]");
            codeBuilder.AppendFormat("    [GeneratedCodeAttribute(\"{0}\", \"{1}\")]",
                                     this.GetType().Name,
                                     typeof(ResXCodeBuilder).Assembly.GetName().Version.ToString());
            codeBuilder.AppendLine();
            codeBuilder.AppendFormat("    {0} static class {1} {{", accessModifier, className);
            codeBuilder.AppendLine();

            foreach (ResXItem resourceItem in resourceItems)
            {
                codeBuilder.AppendLine();
                if (resourceItem.Comment.Length != 0)
                {
                    codeBuilder.AppendFormat("        /// <summary>{0}</summary>", resourceItem.Comment);
                    codeBuilder.AppendLine();
                }
                codeBuilder.AppendFormat("        public static readonly string {0} = null;", resourceItem.Name);
                codeBuilder.AppendLine();
            }

            codeBuilder.AppendLine("    }");
        }

        private static bool IsLocalizedResourceFile(string resourceFileName)
        {
            string locale = ResourceFile.GetLocale(resourceFileName);
            return (string.IsNullOrEmpty(locale) == false);
        }
    }
}