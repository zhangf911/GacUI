﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace DocSymbol
{
    public class ResolveEnvironment
    {
        public HashSet<string> Errors { get; set; }
        public List<GlobalDecl> Globals { get; set; }
        public List<NamespaceDecl> Namespaces { get; set; }
        public Dictionary<string, List<string>> NamespaceReferences { get; set; }
        public Dictionary<string, Dictionary<string, List<SymbolDecl>>> NamespaceContents { get; set; }
        public Dictionary<SymbolDecl, Dictionary<string, List<SymbolDecl>>> SymbolContents { get; set; }
        public Dictionary<TypeDecl, List<SymbolDecl>> ResolvedTypes { get; set; }
        public Dictionary<string, List<SymbolDecl>> AvailableNames { get; set; }

        private void FillNamespaceReferences(SymbolDecl decl)
        {
            var ns = decl as NamespaceDecl;
            if (ns != null)
            {
                this.Namespaces.Add(ns);
                if (decl.Children != null)
                {
                    foreach (var child in decl.Children)
                    {
                        FillNamespaceReferences(child);
                    }
                }
            }
        }

        private void ResolveUsingNamespaces()
        {
            var allns = new HashSet<string>(this.Namespaces.Select(x => x.NameKey).Distinct());
            foreach (var nsg in this.Namespaces.GroupBy(x => x.NameKey))
            {
                var nss = nsg.ToArray();
                var ns = nss[0];
                var nsref = new List<string> { nsg.Key };
                this.NamespaceReferences.Add(nsg.Key, nsref);

                var nslevels = new List<string>();
                {
                    var current = ns;
                    while (current != null)
                    {
                        nslevels.Add(current.NameKey);
                        current = current.Parent as NamespaceDecl;
                    }
                }

                foreach (var uns in nss.SelectMany(x => x.Children).Where(x => x is UsingNamespaceDecl).Cast<UsingNamespaceDecl>())
                {
                    string path = uns.Path.Aggregate("", (a, b) => a + "::" + b);
                    var resolved = nslevels
                        .Select(x => x + path)
                        .Where(x => allns.Contains(x))
                        .ToArray();
                    if (resolved.Length != 1)
                    {
                        Errors.Add(string.Format("Failed to resolve {0} in {1}.", uns.ToString(), ns.ToString()));
                    }
                    foreach (var key in resolved)
                    {
                        if (!nsref.Contains(key))
                        {
                            nsref.Add(key);
                        }
                    }
                }
                foreach (var key in nslevels.Skip(1))
                {
                    if (!nsref.Contains(key))
                    {
                        nsref.Add(key);
                    }
                }
            }

            {
                var nsref = new List<string> { "" };
                this.NamespaceReferences.Add("", nsref);
                foreach (var uns in this.Globals.SelectMany(x => x.Children).Where(x => x is UsingNamespaceDecl).Cast<UsingNamespaceDecl>())
                {
                    string path = uns.Path.Aggregate("", (a, b) => a + "::" + b);
                    if (allns.Contains(path))
                    {
                        if (!nsref.Contains(path))
                        {
                            nsref.Add(path);
                        }
                    }
                    else
                    {
                        Errors.Add(string.Format("(Warning) Failed to resolve {0} in global namespace.", uns.ToString()));
                    }
                }
            }
        }

        private void FillNamespaceContents(string key, IEnumerable<SymbolDecl> symbols)
        {
            var children = symbols
                .Where(x => x.Children != null)
                .SelectMany(x => x.Children)
                .Select(x =>
                    {
                        var template = x as TemplateDecl;
                        return template == null ? x : template.Element;
                    })
                .Where(x => x.Name != null)
                .GroupBy(x => x.Name)
                .ToDictionary(x => x.Key, x => x.GroupBy(y => y.OverloadKey + ";" + y.Tags).Select(y => y.First()).ToList())
                ;
            this.NamespaceContents.Add(key, children);
        }

        private void FillNamespaceContents()
        {
            var nsg = this.Namespaces
                .GroupBy(x => x.NameKey)
                .ToArray();
            foreach (var nss in nsg)
            {
                FillNamespaceContents(nss.Key, nss);
            }
            FillNamespaceContents("", this.Globals);
        }

        private void FillAvailableNames(SymbolDecl symbol)
        {
            if (symbol.Name != null)
            {
                List<SymbolDecl> decls = null;
                if (!this.AvailableNames.TryGetValue(symbol.Name, out decls))
                {
                    decls = new List<SymbolDecl>();
                    this.AvailableNames.Add(symbol.Name, decls);
                }
                decls.Add(symbol);
            }
            if (symbol.Children != null)
            {
                foreach (var child in symbol.Children)
                {
                    FillAvailableNames(child);
                }
            }
        }

        public ResolveEnvironment(IEnumerable<GlobalDecl> globals)
        {
            this.Errors = new HashSet<string>();
            this.Globals = globals.ToList();
            this.Namespaces = new List<NamespaceDecl>();
            this.NamespaceReferences = new Dictionary<string, List<string>>();
            this.NamespaceContents = new Dictionary<string, Dictionary<string, List<SymbolDecl>>>();
            this.SymbolContents = new Dictionary<SymbolDecl, Dictionary<string, List<SymbolDecl>>>();
            this.ResolvedTypes = new Dictionary<TypeDecl, List<SymbolDecl>>();
            this.AvailableNames = new Dictionary<string, List<SymbolDecl>>();

            foreach (var global in globals.SelectMany(x => x.Children))
            {
                FillNamespaceReferences(global);
            }
            ResolveUsingNamespaces();
            FillNamespaceContents();
            foreach (var symbol in this.Globals)
            {
                FillAvailableNames(symbol);
            }
        }

        public TypeDecl FindRefType(TypeDecl type)
        {
            while (type != null)
            {
                var generic = type as GenericTypeDecl;
                if (generic != null)
                {
                    type = generic.Element;
                    continue;
                }
                break;
            }
            return type;
        }

        public Dictionary<string, List<SymbolDecl>> GetSymbolContent(SymbolDecl symbol)
        {
            if (symbol is NamespaceDecl)
            {
                return this.NamespaceContents[symbol.NameKey];
            }
            else
            {
                Dictionary<string, List<SymbolDecl>> content = null;
                if (!this.SymbolContents.TryGetValue(symbol, out content))
                {
                    var template = symbol as TemplateDecl;
                    var typedef = (template == null ? symbol : template.Element) as TypedefDecl;
                    if (typedef != null)
                    {
                        typedef.Type.Resolve(typedef.Parent, this);
                        var refType = FindRefType(typedef.Type);
                        if (refType.ReferencingNameKey == null)
                        {
                            content = null;
                        }
                        else
                        {
                            var symbols = this.ResolvedTypes[refType];
                            content = symbols
                                .Select(x => GetSymbolContent(x))
                                .Where(x => x != null)
                                .SelectMany(x => x)
                                .GroupBy(x => x.Key)
                                .ToDictionary(x => x.Key, x => x.SelectMany(y => y.Value).Distinct().ToList())
                                ;
                        }
                    }
                    else
                    {
                        var visitor = new ResolveSymbolDeclContentVisitor
                        {
                            Environment = this,
                        };
                        symbol.Accept(visitor);
                        content = visitor.Content;
                    }
                    this.SymbolContents.Add(symbol, content);
                }
                return content;
            }
        }

        public void AddXmlError(string messageFormat, string exception, SymbolDecl symbol)
        {
            var template = symbol as TemplateDecl;
            if (template != null)
            {
                symbol = template.Element;
            }

            this.Errors.Add("(Xml) " + string.Format(messageFormat, symbol.OverloadKey) + (exception == null ? "" : "\r\n" + exception));
        }

        public void AddError(bool error, string messageFormat, string name, SymbolDecl symbol)
        {
            var template = symbol as TemplateDecl;
            if (template != null)
            {
                symbol = template.Element;
            }

            this.Errors.Add((error ? "(Error) " : "(Warning) ") + string.Format(messageFormat, name, symbol.OverloadKey));
        }
    }

    class ResolveTypeDeclVisitor : TypeDecl.IVisitor
    {
        public SymbolDecl Symbol { get; set; }
        public ResolveEnvironment Environment { get; set; }
        public bool SupressError { get; set; }

        internal static List<SymbolDecl> FindSymbolInContent(ResolveEnvironment environment, SymbolDecl symbol, TypeDecl decl, string name, Dictionary<string, List<SymbolDecl>> content, bool typeAndNamespaceOnly, bool addError)
        {
            if (content == null)
            {
                return null;
            }

            List<SymbolDecl> decls = null;
            if (content.TryGetValue(name, out decls))
            {
                if (typeAndNamespaceOnly)
                {
                    decls = decls
                        .Where(x => !(x is FuncDecl) && !(x is VarDecl) && !(x is EnumDecl))
                        .ToList();
                    if (decls.Count == 0)
                    {
                        return null;
                    }
                }
                var nameKeys = decls.Select(x => x.NameKey).Distinct().ToList();
                var overloadKeys = decls.Select(x => x.OverloadKey).Distinct().ToList();
                if (overloadKeys.Count > 0)
                {
                    decl.ReferencingOverloadKeys = overloadKeys;
                }
                if (nameKeys.Count > 1)
                {
                    if (addError)
                    {
                        var printingKeys = overloadKeys.Aggregate("", (a, b) => a + "\r\n" + b);
                        environment.AddError(false, "Found multiple symbols for {0} in {1}: " + printingKeys, name, symbol);
                    }
                    return null;
                }
                decl.ReferencingNameKey = nameKeys[0];
                return decls;
            }
            return null;
        }

        private List<SymbolDecl> FindSymbolInContent(TypeDecl decl, string name, Dictionary<string, List<SymbolDecl>> content, bool typeAndNamespaceOnly, bool addError)
        {
            return FindSymbolInContent(this.Environment, this.Symbol, decl, name, content, typeAndNamespaceOnly, addError);
        }

        private void AddError(TypeDecl decl, string name)
        {
            List<SymbolDecl> decls = null;
            if (this.Environment.AvailableNames.TryGetValue(name, out decls))
            {
                decls = decls
                    .Where(x => !(x is FuncDecl) && !(x is VarDecl) && !(x is EnumDecl))
                    .ToList();
                if (decls.Count == 0)
                {
                    if (!this.SupressError)
                    {
                        this.Environment.AddError(false, "Failed to resolve {0} in {1}.", name, this.Symbol);
                    }
                }
                else
                {
                    decl.ReferencingOverloadKeys = decls
                        .Select(x => x.OverloadKey)
                        .Distinct()
                        .ToList();
                    var printingKeys = decl.ReferencingOverloadKeys.Aggregate("", (a, b) => a + "\r\n" + b);
                    if (!this.SupressError)
                    {
                        this.Environment.AddError(false, "Failed to resolve {0} in {1}, treated as a open type:" + printingKeys, name, this.Symbol);
                    }
                }
            }
            else
            {
                if (!this.SupressError)
                {
                    this.Environment.AddError(true, "Failed to resolve {0} in {1}.", name, this.Symbol);
                }
            }
        }

        public void Visit(RefTypeDecl decl)
        {
            switch (decl.Name)
            {
                case "__int8":
                case "__int16":
                case "__int32":
                case "__int64":
                case "char":
                case "wchar_t":
                case "bool":
                case "float":
                case "double":
                case "void":
                case "int":
                case "long":
                    return;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                var current = this.Symbol;
                while (current != null)
                {
                    if (!(current is TypedefDecl))
                    {
                        if (!(current is NamespaceDecl) && !(current is GlobalDecl))
                        {
                            var content = this.Environment.GetSymbolContent(current);
                            var decls = FindSymbolInContent(decl, decl.Name, content, pass == 0, pass == 1 && !this.SupressError);
                            if (decls != null)
                            {
                                this.Environment.ResolvedTypes.Add(decl, decls);
                                return;
                            }
                        }
                        else
                        {
                            var references = this.Environment.NamespaceReferences[current is GlobalDecl ? "" : current.NameKey];
                            foreach (var reference in references)
                            {
                                var content = this.Environment.NamespaceContents[reference];
                                var decls = FindSymbolInContent(decl, decl.Name, content, pass == 0, pass == 1 && !this.SupressError);
                                if (decls != null)
                                {
                                    this.Environment.ResolvedTypes.Add(decl, decls);
                                    return;
                                }
                            }
                        }
                    }
                    current = current.Parent;
                }
            }

            AddError(decl, decl.Name);
        }

        public void Visit(SubTypeDecl decl)
        {
            decl.Parent.Resolve(this.Symbol, this.Environment);
            var refType = this.Environment.FindRefType(decl.Parent);
            if (refType.ReferencingNameKey != null)
            {
                Dictionary<string, List<SymbolDecl>> content = null;
                if (!this.Environment.NamespaceContents.TryGetValue(refType.ReferencingNameKey, out content))
                {
                    var parentDecls = this.Environment.ResolvedTypes[refType];
                    content = parentDecls
                        .Select(x => this.Environment.GetSymbolContent(x))
                        .Where(x => x != null)
                        .SelectMany(x => x)
                        .GroupBy(x => x.Key)
                        .ToDictionary(x => x.Key, x => x.SelectMany(y => y.Value).ToList())
                        ;
                }
                var decls = FindSymbolInContent(decl, decl.Name, content, true, false);
                if (decls == null)
                {
                    decls = FindSymbolInContent(decl, decl.Name, content, false, !this.SupressError);
                }
                if (decls != null)
                {
                    this.Environment.ResolvedTypes.Add(decl, decls);
                    return;
                }
            }

            AddError(decl, decl.Name);
        }

        public void Visit(DecorateTypeDecl decl)
        {
            decl.Element.Resolve(this.Symbol, this.Environment);
        }

        public void Visit(ArrayTypeDecl decl)
        {
            decl.Element.Resolve(this.Symbol, this.Environment);
        }

        public void Visit(FunctionTypeDecl decl)
        {
            decl.ReturnType.Resolve(this.Symbol, this.Environment);
            foreach (var type in decl.Parameters)
            {
                if (type.Parent == null)
                {
                    type.Type.Resolve(this.Symbol, this.Environment);
                }
                else
                {
                    type.Resolve(this.Environment);
                }
            }
        }

        public void Visit(ClassMemberTypeDecl decl)
        {
            decl.Element.Resolve(this.Symbol, this.Environment);
            decl.ClassType.Resolve(this.Symbol, this.Environment);
        }

        public void Visit(GenericTypeDecl decl)
        {
            decl.Element.Resolve(this.Symbol, this.Environment);
            foreach (var type in decl.TypeArguments)
            {
                type.Resolve(this.Symbol, this.Environment);
            }
        }

        public void Visit(DeclTypeDecl decl)
        {
        }

        public void Visit(VariadicArgumentTypeDecl decl)
        {
            decl.Element.Resolve(this.Symbol, this.Environment);
        }

        public void Visit(ConstantTypeDecl decl)
        {
        }
    }

    class ResolveSymbolDeclVisitor : SymbolDecl.IVisitor
    {
        private static Regex regexSymbol = new Regex(@"\[(?<type>[TFM]):(?<symbol>[^\]]*)\]");

        public ResolveEnvironment Environment { get; set; }

        private XElement ResolveCommentSymbol(SymbolDecl decl, string name)
        {
            var type = name
                .Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries)
                .Aggregate<string, TypeDecl>(null, (a, b) =>
                {
                    return a == null
                        ? (TypeDecl)new RefTypeDecl { Name = b }
                        : (TypeDecl)new SubTypeDecl { Parent = a, Name = b }
                        ;
                })
                ;
            type.Resolve(decl, this.Environment, true);
            if (type.ReferencingOverloadKeys == null)
            {
                this.Environment.AddXmlError("Failed to resolve symbol \"" + name + "\" in XML comment for {0}.", null, decl);
                return null;
            }
            else
            {
                return new XElement("links",
                    type.ReferencingOverloadKeys
                        .Select(x => new XElement("link", new XAttribute("cref", x)))
                    );
            }
        }

        private IEnumerable<XNode> ResolveCommentText(SymbolDecl decl, string text)
        {
            var matches = regexSymbol.Matches(text).Cast<Match>().ToArray();
            var linkXmls = new List<XElement>();
            foreach (var match in matches)
            {
                var type = match.Groups["type"].Value;
                var symbol = match.Groups["symbol"].Value;
                var symbolName = symbol
                    .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x =>
                    {
                        int index = x.IndexOf('`');
                        return index == -1 ? x : x.Substring(0, index);
                    })
                    .Aggregate((a, b) => a + "::" + b)
                    ;
                var links = ResolveCommentSymbol(decl, symbolName);
                linkXmls.Add(links);
            }

            int lastIndex = 0;
            for (int i = 0; i < matches.Length; i++)
            {
                var match = matches[i];
                if (match.Index != lastIndex)
                {
                    yield return new XText(text.Substring(lastIndex, match.Index - lastIndex));
                    lastIndex = match.Index + match.Length;
                }

                if (linkXmls[i] == null)
                {
                    yield return new XText(match.Value);
                }
                else
                {
                    yield return linkXmls[i];
                }
            }

            if (text.Length != lastIndex)
            {
                yield return new XText(text.Substring(lastIndex, text.Length - lastIndex));
            }
        }

        private IEnumerable<XNode> ResolveCommentNode(SymbolDecl decl, XNode node)
        {
            var text = node as XText;
            var cdata = node as XCData;
            var element = node as XElement;
            if (text != null)
            {
                var replacement = ResolveCommentText(decl, text.Value).ToArray();
                return replacement;
            }
            if (cdata != null)
            {
                var replacement = ResolveCommentText(decl, cdata.Value).ToArray();
                return replacement;
            }
            if (element != null)
            {
                if (element.Name == "see")
                {
                    var att = element.Attribute("cref");
                    var replacement = ResolveCommentSymbol(decl, att.Value);
                    return new XNode[] { replacement == null ? element : replacement };
                }
                else
                {
                    foreach (var child in element.Nodes().ToArray())
                    {
                        ResolveCommentNode(decl, child);
                    }
                    var replacement = element.Nodes()
                        .SelectMany(x => ResolveCommentNode(decl, x))
                        .ToArray();
                    element.ReplaceNodes(replacement);
                    return new XNode[] { element };
                }
            }
            else
            {
                return new XNode[] { node };
            }
        }

        public void ResolveComment(SymbolDecl decl)
        {
            if (decl.Document != null)
            {
                try
                {
                    var xml = XElement.Parse("<Document>" + decl.Document + "</Document>", LoadOptions.PreserveWhitespace);

                    var template = decl as TemplateDecl;
                    var symbol = decl;
                    if (template == null)
                    {
                        template = decl.Parent as TemplateDecl;
                    }
                    else
                    {
                        symbol = template.Element;
                    }

                    var typeparamXmls = xml.Elements("typeparam").ToArray();
                    var expectedTypeparamNames = template == null ? new string[0] : template.TypeParameters.Select(x => x.Name).ToArray();
                    var actualTypeparamNames = typeparamXmls.Select(x => x.Attribute("name").Value).ToArray();
                    if (!expectedTypeparamNames.SequenceEqual(actualTypeparamNames))
                    {
                        this.Environment.AddXmlError("<typeparam> elements do not match type parameter names in order in {0}", null, symbol);
                    }

                    var paramXmls = xml.Elements("param").ToArray();
                    var func = symbol as FuncDecl;
                    var expectedParamNames = func == null || func.Children == null ? new string[0] : func.Children.Select(x => x.Name).ToArray();
                    var actualParamNames = paramXmls.Select(x => x.Attribute("name").Value).ToArray();
                    if (!expectedParamNames.SequenceEqual(actualParamNames))
                    {
                        this.Environment.AddXmlError("<param> elements do not match parameter names in order in {0}", null, symbol);
                    }

                    var returnXmls = xml.Elements("returns").ToArray();
                    if (returnXmls.Length == 1 ^ (func != null && ((FunctionTypeDecl)func.Type).ReturnType.ToString() != "void"))
                    {
                        this.Environment.AddXmlError("<returns> element does not math the function return type in {0}", null, symbol);
                    }

                    ResolveCommentNode(symbol, xml);
                    decl.Document = xml.ToString();
                }
                catch (XmlException ex)
                {
                    this.Environment.AddXmlError("Failed to parse XML comment for {0}.", ex.Message, decl);
                }
            }
        }

        public void Visit(GlobalDecl decl)
        {
        }

        public void Visit(NamespaceDecl decl)
        {
        }

        public void Visit(UsingNamespaceDecl decl)
        {
        }

        public void Visit(TypeParameterDecl decl)
        {
        }

        public void Visit(TemplateDecl decl)
        {
            ResolveComment(decl);
            foreach (var type in decl.Specialization)
            {
                type.Resolve(decl, this.Environment);
            }
        }

        public void Visit(BaseTypeDecl decl)
        {
            ResolveComment(decl);
        }

        public void Visit(ClassDecl decl)
        {
            ResolveComment(decl);
            var templateDecl = decl.Parent as TemplateDecl;
            if (templateDecl != null)
            {
                Visit(templateDecl);
            }

            foreach (var baseType in decl.BaseTypes)
            {
                baseType.Type.Resolve(decl.Parent, this.Environment);
            }
        }

        public void Visit(VarDecl decl)
        {
            ResolveComment(decl);
            decl.Type.Resolve(decl, this.Environment);
        }

        public void Visit(FuncDecl decl)
        {
            ResolveComment(decl);
            var templateDecl = decl.Parent as TemplateDecl;
            if (templateDecl != null)
            {
                Visit(templateDecl);
            }

            decl.Type.Resolve(decl, this.Environment);
        }

        public void Visit(GroupedFieldDecl decl)
        {
            ResolveComment(decl);
        }

        public void Visit(EnumItemDecl decl)
        {
            ResolveComment(decl);
        }

        public void Visit(EnumDecl decl)
        {
            ResolveComment(decl);
        }

        public void Visit(TypedefDecl decl)
        {
            ResolveComment(decl);
            var templateDecl = decl.Parent as TemplateDecl;
            if (templateDecl != null)
            {
                Visit(templateDecl);
            }

            decl.Type.Resolve(decl, this.Environment);
        }
    }

    class ResolveSymbolDeclContentVisitor : SymbolDecl.IVisitor
    {
        public ResolveEnvironment Environment { get; set; }
        public Dictionary<string, List<SymbolDecl>> Content { get; set; }

        private void AddSymbol(string key, SymbolDecl symbol)
        {
            if (this.Content == null)
            {
                this.Content = new Dictionary<string, List<SymbolDecl>>();
            }

            List<SymbolDecl> decls = null;
            if (!this.Content.TryGetValue(key, out decls))
            {
                decls = new List<SymbolDecl>();
                this.Content.Add(key, decls);
            }
            if (!decls.Contains(symbol))
            {
                decls.Add(symbol);
            }
        }

        public void Visit(GlobalDecl decl)
        {
        }

        public void Visit(NamespaceDecl decl)
        {
        }

        public void Visit(UsingNamespaceDecl decl)
        {
        }

        public void Visit(TypeParameterDecl decl)
        {
        }

        public void Visit(TemplateDecl decl)
        {
            foreach (var item in decl.TypeParameters)
            {
                AddSymbol(item.Name, item);
            }
        }

        public void Visit(BaseTypeDecl decl)
        {
        }

        public void Visit(ClassDecl decl)
        {
            if (decl.Children != null)
            {
                foreach (var item in decl.Children)
                {
                    var enumClass = item as EnumDecl;
                    if (enumClass != null && !enumClass.EnumClass)
                    {
                        AddSymbol(item.Name, item);
                        item.Accept(this);
                    }

                    if (item.Name != null)
                    {
                        var template = item as TemplateDecl;
                        var func = (template == null ? item : template.Element) as FuncDecl;
                        if (func == null || (func.Function != Function.Constructor && func.Function != Function.Destructor))
                        {
                            AddSymbol(item.Name, item);
                        }
                    }
                    else
                    {
                        item.Accept(this);
                    }
                }
            }

            var keys = this.Content == null ? null : new HashSet<string>(this.Content.Keys);
            foreach (var baseType in decl.BaseTypes)
            {
                baseType.Type.Resolve(decl.Parent, this.Environment);
                var refType = this.Environment.FindRefType(baseType.Type);
                if (refType.ReferencingNameKey != null)
                {
                    var symbols = this.Environment.ResolvedTypes[refType];
                    foreach (var symbol in symbols)
                    {
                        if (symbol == decl)
                        {
                            break;
                        }

                        var content = this.Environment.GetSymbolContent(symbol);
                        if (content != null)
                        {
                            foreach (var item in content.Where(p => keys == null || !keys.Contains(p.Key)).SelectMany(x => x.Value))
                            {
                                if (item.Access != Access.Private)
                                {
                                    var template = item as TemplateDecl;
                                    var func = (template == null ? item : template.Element) as FuncDecl;
                                    if (func == null || (func.Function != Function.Constructor && func.Function != Function.Destructor))
                                    {
                                        AddSymbol(item.Name, item);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public void Visit(VarDecl decl)
        {
        }

        public void Visit(FuncDecl decl)
        {
        }

        public void Visit(GroupedFieldDecl decl)
        {
            if (decl.Children != null)
            {
                foreach (var item in decl.Children)
                {
                    item.Accept(this);
                }
            }
        }

        public void Visit(EnumItemDecl decl)
        {
        }

        public void Visit(EnumDecl decl)
        {
            foreach (EnumItemDecl item in decl.Children)
            {
                AddSymbol(item.Name, item);
            }
        }

        public void Visit(TypedefDecl decl)
        {
        }
    }
}
