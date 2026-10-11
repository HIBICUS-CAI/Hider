using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// Base for methods implemented by one embedded SkSL shader named after <see cref="Id"/>. Every <see cref="Params"/> key
    /// is passed as a float uniform with its effective value, plus the kind's fixed uniforms (<see cref="CommonParams.FixedUniforms"/>).
    /// Mosaic methods receive the capture as the child shader "src".
    /// </summary>
    public abstract class SkslMethod : IMosaicMethod
    {
        private SKRuntimeEffect? effect;

        public abstract string Id { get; }
        public abstract string LabelKey { get; }
        public abstract MethodKind Kind { get; }
        public virtual PlatformSupport Support => PlatformSupport.All;
        public abstract IReadOnlyList<ParamSpec> Params { get; }

        /// <summary>The compiled shader, created on first use and shared by every draw.</summary>
        public SKRuntimeEffect Effect => effect ??= ShaderSource.Compile(Id);

        public SKShader CreateShader(MosaicInputs _inputs, ParamValues _p)
        {
            if (Kind == MethodKind.Mosaic && _inputs.Source is null)
            {
                throw new ArgumentException($"{Id} needs a captured image", nameof(_inputs));
            }
            // A pass-through shader costs a full-resolution SkSL evaluation on the CPU (tens of ms per 1080p frame) for an
            // unchanged picture: hand the child over directly (RegistryTests.PassThrough_MatchesTheShader checks both give the same pixels).
            return Kind == MethodKind.Mosaic && IsPassThrough(_p) ? SourceShader(_inputs, _p) : CreateEffectShader(_inputs, _p);
        }

        /// <summary>The method's SkSL shader, even when <see cref="IsPassThrough"/> would skip it.</summary>
        internal SKShader CreateEffectShader(MosaicInputs _inputs, ParamValues _p)
        {
            var uniforms = new SKRuntimeEffectUniforms(Effect);
            foreach (var spec in Params)
            {
                uniforms[spec.Key] = (float)UniformValue(spec.Key, _p);
            }
            uniforms[CommonParams.AreaSizeUniform] = new[] { (float)_inputs.AreaSize.Width, _inputs.AreaSize.Height };
            uniforms[CommonParams.AreaOriginUniform] = new[] { (float)_inputs.AreaOrigin.X, _inputs.AreaOrigin.Y };
            if (Kind == MethodKind.Generated)
            {
                // Kept small so the shader sees it exactly as a float.
                uniforms[CommonParams.SeedUniform] = (float)(((long)_p.Raw(CommonParams.Seed) % 65536 + 65536) % 65536);
                uniforms[CommonParams.BaseColorUniform] = new[]
                {
                    (float)_p.Raw(CommonParams.BaseColorR), (float)_p.Raw(CommonParams.BaseColorG), (float)_p.Raw(CommonParams.BaseColorB),
                };
            }
            var children = new SKRuntimeEffectChildren(Effect);
            if (Kind == MethodKind.Mosaic)
            {
                children["src"] = SourceShader(_inputs, _p);
            }
            return Effect.ToShader(uniforms, children);
        }

        /// <summary>True when the shader returns <c>src.eval(p)</c> for these values (mosaic methods only).</summary>
        protected internal virtual bool IsPassThrough(ParamValues _p)
        {
            return false;
        }

        /// <summary>Uniform value for a parameter; the effective value unless a method needs its own rounding.</summary>
        protected virtual double UniformValue(string _key, ParamValues _p)
        {
            return _p.Effective(_key);
        }

        /// <summary>The child shader "src" (in area pixel coordinates). Default: the capture with nearest sampling.</summary>
        protected virtual SKShader SourceShader(MosaicInputs _inputs, ParamValues _p)
        {
            return _inputs.Source!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest));
        }
    }
}
