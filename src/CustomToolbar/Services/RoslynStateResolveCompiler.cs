﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2020 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xarial.CadPlus.CustomToolbar.Base;
using Xarial.CadPlus.CustomToolbar.Exceptions;
using Xarial.CadPlus.CustomToolbar.Structs;
using Xarial.CadPlus.ExtensionModule;
using Xarial.XCad;
using Xarial.XCad.Base;

namespace Xarial.CadPlus.CustomToolbar.Services
{

    public partial class CommandsManager
    {
        public abstract class RoslynStateResolveCompiler : IStateResolveCompiler 
        {
            private readonly string m_CodeTemplate;
            private readonly IXApplication m_App;

            public RoslynStateResolveCompiler(string codeTemplate, IXApplication app) 
            {
                m_CodeTemplate = codeTemplate;

                m_App = app;
            }

            public Dictionary<CommandMacroInfo, IToggleButtonStateResolver> CreateResolvers(
                IEnumerable<CommandMacroInfo> macroInfos)
            {
                var retVal = new Dictionary<CommandMacroInfo, IToggleButtonStateResolver>();

                var opts = CreateCompilationOptions()
                    .WithOptimizationLevel(OptimizationLevel.Release);

                var refs = new List<MetadataReference>(GetReferences());
                refs.AddRange(new[]
                {
                    MetadataReference.CreateFromFile(typeof(Debug).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(AppDomain).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IToggleButtonStateResolver).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IXApplication).Assembly.Location)
                });
                
                var syntaxTrees = new List<SyntaxTree>();
                var classToMacroMap = new Dictionary<string, CommandMacroInfo>();

                foreach (var macroInfo in macroInfos) 
                {
                    var className = "CT_" + CreateUniqueMemberName();
                    var code = string.Format(m_CodeTemplate, className, macroInfo.ToggleButtonStateCode);
                    var syntaxTree = CreateSyntaxTree(SourceText.From(code, Encoding.UTF8));
                    syntaxTrees.Add(syntaxTree);
                    classToMacroMap.Add(className, macroInfo);
                }

                var dllName = "Assm_" + CreateUniqueMemberName() + ".dll";
                var compilation = CreateCompilation(syntaxTrees, dllName, refs, opts);

                Assembly assm = null;

                using (var assmStream = new MemoryStream())
                {
                    var result = compilation.Emit(assmStream);

                    if (result.Success)
                    {
                        assm = Assembly.Load(assmStream.GetBuffer());

                        Assembly AssemblyResolve(object sender, ResolveEventArgs args)
                        {
                            var resolvedAssm = AppDomain.CurrentDomain.GetAssemblies()
                                .FirstOrDefault(a => string.Equals(a.GetName().FullName, args.Name));

                            return resolvedAssm;
                        }

                        AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolve;

                        foreach (var type in assm.GetTypes().Where(t => typeof(IToggleButtonStateResolver).IsAssignableFrom(t)))
                        {
                            if (classToMacroMap.TryGetValue(type.Name, out CommandMacroInfo macroInfo))
                            {
                                var stateResolver = (IToggleButtonStateResolver)Activator.CreateInstance(type, m_App);
                                retVal.Add(macroInfo, stateResolver);
                            }
                            else
                            {
                                Debug.Assert(false, "Unregistered type");
                            }
                        }

                        AppDomain.CurrentDomain.AssemblyResolve -= AssemblyResolve;
                    }
                    else 
                    {
                        throw new CustomResolverCodeCompileFailedException(result.Diagnostics);
                    }
                }

                return retVal;
            }

            private string CreateUniqueMemberName() => Guid.NewGuid().ToString().TrimStart('{').TrimEnd('}').Replace("-", "");

            protected abstract CompilationOptions CreateCompilationOptions();
            protected abstract IEnumerable<MetadataReference> GetReferences();
            protected abstract SyntaxTree CreateSyntaxTree(SourceText src);
            protected abstract Compilation CreateCompilation(IEnumerable<SyntaxTree> code, string dllName, IEnumerable<MetadataReference> refs, CompilationOptions opts);
        }
    }
}
