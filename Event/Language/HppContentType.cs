using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    internal static class HppContentType
    {
        public const string Name = "hpp";

        [Export]
        [Name(Name)]
        [BaseDefinition("code")]
        internal static ContentTypeDefinition HppContentTypeDefinition;

        // .hxx 本身实测会被装了 C++ 工作负载的 VS 强关联到内置 C/C++ 语言服务（legacy
        // IVsLanguageInfo 注册，比这里的 MEF 内容类型优先级更高，我们的分类器/诊断永远抢不过它），
        // 所以哈++脚本改用不会跟任何主流语言冲突的 .phxx。
        [Export]
        [FileExtension(".phxx")]
        [ContentType(Name)]
        internal static FileExtensionToContentTypeDefinition HppFileExtensionDefinition;
    }
}
