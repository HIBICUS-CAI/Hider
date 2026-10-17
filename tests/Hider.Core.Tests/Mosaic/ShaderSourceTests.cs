using System.Text.RegularExpressions;
using Hider.Core.Mosaic;

namespace Hider.Core.Tests.Mosaic
{
    public partial class ShaderSourceTests
    {
        private static readonly string[] embeddedIds = { "block", "blur", "hexcell", "noise", "pixelate" };
        private static readonly string[] sourceChild = { "src" };

        [Fact]
        public void CT133_All_EveryEmbeddedShaderInOrdinalOrder()
        {
            Assert.Equal(embeddedIds, ShaderSource.All());
        }

        [Fact]
        public void CT133_All_OnlySkslResourcesOfTheMethodsFolder()
        {
            var names = typeof(ShaderSource).Assembly.GetManifestResourceNames();
            var expected = names.Where(_n => _n.StartsWith("Hider.Core.Mosaic.Methods.", StringComparison.Ordinal) && _n.EndsWith(".sksl", StringComparison.Ordinal)).Count();
            Assert.Equal(expected, ShaderSource.All().Count);
        }

        [Theory]
        [InlineData("block")]
        [InlineData("blur")]
        [InlineData("hexcell")]
        [InlineData("noise")]
        [InlineData("pixelate")]
        public void CT133_Load_TheFileText(string _id)
        {
            string path = Path.Combine(Imaging.Golden.RepoRoot, "src", "Hider.Core", "Mosaic", "Methods", _id + ".sksl");
            Assert.Equal(File.ReadAllText(path), ShaderSource.Load(_id));
        }

        [Theory]
        [InlineData("does-not-exist")]
        [InlineData("Block")]          // resource names are case sensitive
        [InlineData("block.sksl")]     // the suffix is added
        [InlineData("")]
        public void CT133_Load_Unknown_Throws(string _id)
        {
            var e = Assert.Throws<InvalidOperationException>(() => ShaderSource.Load(_id));
            Assert.Equal($"Missing embedded shader {_id}.sksl", e.Message);
            Assert.Throws<InvalidOperationException>(() => ShaderSource.Compile(_id));
        }

        [Fact]
        public void CT14_Compile_EveryShader_ANewEffectEachTime()
        {
            foreach (string id in ShaderSource.All())
            {
                using var a = ShaderSource.Compile(id);
                using var b = ShaderSource.Compile(id);
                Assert.NotSame(a, b);
                Assert.Equal(a.Uniforms.ToArray(), b.Uniforms.ToArray());
                Assert.True(a.UniformSize > 0);
            }
        }

        /// <summary>
        /// CLAUDE.md: each shader starts with comments listing its uniforms and children. The lists, the declarations, the
        /// compiled effect and the method's ParamSpec keys plus the kind's fixed uniforms must all agree, in order.
        /// </summary>
        [Theory]
        [InlineData("block")]
        [InlineData("blur")]
        [InlineData("hexcell")]
        [InlineData("noise")]
        [InlineData("pixelate")]
        public void CT15_HeaderComment_ListsTheDeclaredUniformsAndChildren(string _id)
        {
            string source = ShaderSource.Load(_id);
            var (uniforms, children) = ParseHeader(source);

            var declared = DeclarationRegex().Matches(source).Select(_m => (Type: _m.Groups[1].Value, Name: _m.Groups[2].Value)).ToList();
            Assert.Equal(uniforms, declared.Where(_d => _d.Type != "shader").Select(_d => _d.Name));
            Assert.Equal(children, declared.Where(_d => _d.Type == "shader").Select(_d => _d.Name));

            using var effect = ShaderSource.Compile(_id);
            Assert.Equal(uniforms, effect.Uniforms);
            Assert.Equal(children, effect.Children);

            var method = MethodRegistry.Find(_id)!;
            var expected = method.Params.Select(_p => _p.Key).Concat(CommonParams.FixedUniforms(method.Kind)).Order(StringComparer.Ordinal);
            Assert.Equal(expected, uniforms.Order(StringComparer.Ordinal));
            Assert.Equal(method.Kind == MethodKind.Mosaic ? sourceChild : Array.Empty<string>(), children);
        }

        [Theory]
        [InlineData("block")]
        [InlineData("blur")]
        [InlineData("hexcell")]
        [InlineData("noise")]
        [InlineData("pixelate")]
        public void CT15_HeaderComment_FirstLineNamesTheMethod(string _id)
        {
            string first = ShaderSource.Load(_id).Split('\n')[0];
            Assert.StartsWith($"// {_id}", first, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("block", "float", "cellSize", "variance", "gap")]
        [InlineData("noise", "float", "scale", "levels")]
        [InlineData("pixelate", "float", "block")]
        [InlineData("hexcell", "float", "radius")]
        [InlineData("blur", "float", "radius")]
        public void CT15_ParamUniforms_AreFloats(string _id, string _type, params string[] _keys)
        {
            // SkslMethod sets every ParamSpec key as one float.
            var declared = DeclarationRegex().Matches(ShaderSource.Load(_id)).ToDictionary(_m => _m.Groups[2].Value, _m => _m.Groups[1].Value);
            Assert.All(_keys, _k => Assert.Equal(_type, declared[_k]));
            Assert.Equal("float2", declared[CommonParams.AreaSizeUniform]);
            Assert.Equal("float2", declared[CommonParams.AreaOriginUniform]);
            if (MethodRegistry.Find(_id)!.Kind == MethodKind.Generated)
            {
                Assert.Equal("float3", declared[CommonParams.BaseColorUniform]);
                Assert.Equal("float3", declared[CommonParams.SeedUniform]);
            }
        }

        /// <summary>The names in the "// uniforms:" and "// children:" comments (continuation lines included).</summary>
        internal static (List<string> Uniforms, List<string> Children) ParseHeader(string _source)
        {
            var lines = _source.Split('\n').Select(_l => _l.TrimEnd('\r')).TakeWhile(_l => _l.StartsWith("//", StringComparison.Ordinal)).Select(_l => _l[2..].Trim()).ToList();
            int u = lines.FindIndex(_l => _l.StartsWith("uniforms:", StringComparison.Ordinal));
            int c = lines.FindIndex(_l => _l.StartsWith("children:", StringComparison.Ordinal));
            Assert.True(u >= 0, "no uniforms: line");
            Assert.True(c > u, "no children: line after the uniforms");
            string uniformText = string.Join(" ", lines.Skip(u).Take(c - u))["uniforms:".Length..];
            string childText = string.Join(" ", lines.Skip(c))["children:".Length..].Trim();
            return (Names(uniformText), childText == "none" ? new List<string>() : Names(childText));
        }

        /// <summary>The leading identifier of each top-level comma-separated item ("a (x, y), b" gives a, b).</summary>
        private static List<string> Names(string _text)
        {
            var items = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i <= _text.Length; i++)
            {
                char ch = i < _text.Length ? _text[i] : ',';
                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    depth--;
                }
                else if (ch == ',' && depth == 0)
                {
                    items.Add(_text[start..i]);
                    start = i + 1;
                }
            }
            Assert.Equal(0, depth);
            return items.Select(_i => IdentifierRegex().Match(_i)).Where(_m => _m.Success).Select(_m => _m.Value).ToList();
        }

        [GeneratedRegex(@"^\s*uniform\s+(\w+)\s+(\w+)\s*;", RegexOptions.Multiline)]
        private static partial Regex DeclarationRegex();

        [GeneratedRegex(@"(?<=^\s*)[A-Za-z_]\w*")]
        private static partial Regex IdentifierRegex();
    }
}
