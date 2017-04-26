using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DbUp.Engine;
using DbUp.Engine.Transactions;

namespace DbUp.Support.SqlServer.Scripting
{
    /// <summary>
    /// This script provider will collect all the scripts in the generated Definitions folder and attempt to order
    /// them in order of dependency, the dependency searching is quite basic, as it only looks for
    /// references of named entries, however this could be easily extended
    /// </summary>
    public class DefinitionScriptProvider : IScriptProvider
    {
        private readonly string _defintionFolderPath;
        private readonly Regex _contentReplaceRegex;

        public DefinitionScriptProvider(): this("..\\..\\Definitions", "(SET ANSI_NULLS ON)|(SET ANSI_NULLS OFF)|(SET QUOTED_IDENTIFIER OFF)|(SET QUOTED_IDENTIFIER ON)")
        {
            
        }

        public DefinitionScriptProvider(string defintionFolderPath = "..\\..\\Definitions", 
            string contentReplaceRegex = "(SET ANSI_NULLS ON)|(SET ANSI_NULLS OFF)|(SET QUOTED_IDENTIFIER OFF)|(SET QUOTED_IDENTIFIER ON)")
        {
            if (Directory.Exists(defintionFolderPath))
            {
                _defintionFolderPath = new DirectoryInfo(defintionFolderPath).FullName;
            }
            if (_defintionFolderPath == null)
            {
                var possible = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), defintionFolderPath);
                if (Directory.Exists(possible))
                {
                    _defintionFolderPath = new DirectoryInfo(possible).FullName;
                }
            }

            if (_defintionFolderPath == null)
            {
                throw new DirectoryNotFoundException($"Path: {defintionFolderPath} not found");
            }

            _contentReplaceRegex = new Regex(contentReplaceRegex, RegexOptions.Multiline);
        }

        public IEnumerable<SqlScript> GetScripts(IConnectionManager connectionManager)
        {
            return GatherScriptsWithDependencies();
        }

        public string GetScriptContent()
        {
            var scripts = GatherScriptsWithDependencies();
            return string.Join($"{Environment.NewLine} GO {Environment.NewLine}", scripts.Select(x => x.Contents));
        }

        private string GetFileContent(string path)
        {
            var text = File.ReadAllText(path);
            return _contentReplaceRegex.Replace(text, string.Empty);
        }

        private IEnumerable<Script> GatherScripts()
        {
            return Directory.GetFiles(_defintionFolderPath, "*.sql", SearchOption.AllDirectories)
                .Select(x => new Script
                {
                    Contents = GetFileContent(x),
                    Path = x,
                });
        }

        private Script DiscoverDependants(IEnumerable<Script> source, Script leaf)
        {
            //insepct the contents of the leaf for any mentions of other entries
            leaf.SetDependencies(source
                //ignore the current leaf
                .Where(x => !x.Equals(leaf))
                //this might need to be extended it is a very basic example of finding the dependency
                .Where(x => leaf.Contents.Contains(x.CompareName)));
            return leaf;
        }

        private IEnumerable<SqlScript> GatherScriptsWithDependencies()
        {
            var source = GatherScripts().ToList();
            var scripts = source.Select(x => DiscoverDependants(source, x)).ToList();
            return WalkDependencies(scripts).Select(x => new SqlScript($"Definition_{x.Name}", x.Contents));
        }

        private IEnumerable<Script> WalkDependencies(List<Script> scripts)
        {
            //Aggregate all the scripts by walking up the parents
            return scripts.Aggregate(new List<Script>(), AddParent);
        }

        private List<Script> AddParent(List<Script> result, Script current)
        {
            foreach (var currentDependency in current?.Dependencies ?? new List<Script>())
            {
                result = AddParent(result, currentDependency);
                if (!result.Contains(currentDependency))
                {
                    result.Add(currentDependency);
                }
            }
            if (!result.Contains(current))
            {
                result.Add(current);
            }
            return result;
        }

        class Script
        {
            public string Path { get; set; }
            public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
            public string CompareName => string.Join(".", Name.Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries).Select(y => $"[{y}]"));
            public string Contents { get; set; }
            public IEnumerable<Script> Dependencies { get; private set; }
            public void SetDependencies(IEnumerable<Script> dependencies)
            {
                Dependencies = dependencies;
            }

            public override bool Equals(object obj)
            {
                var compare = obj as Script;
                return compare?.Path.Equals(Path) ?? false;
            }

            public override int GetHashCode()
            {
                return Path.GetHashCode();
            }
        }

        class ContentReplacement
        {
            public string FindText { get; set; }
            public string ReplaceText { get; set; }
        }
    }
}
