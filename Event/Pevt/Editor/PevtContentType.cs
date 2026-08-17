using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace PolarisTools.Event.Pevt.Editor
{
    /// <summary>
    /// <c>.pevt</c> 的编辑器内容类型定义。
    ///
    /// 以 <c>code</c> 为基类型，这样 Visual Studio 自带的行为（大纲、缩进、书签、注释框选、
    /// 查找栏的代码感知）直接生效，不必逐项重造。
    /// </summary>
    internal static class PevtContentType
    {
        /// <summary>内容类型名。分类器、tagger 与后续补全都按它挂载。</summary>
        public const string Name = "pevt";

        public const string FileExtension = ".pevt";

#pragma warning disable 649 // MEF 通过特性导出定义字段，赋值由组合容器完成。
        [Export]
        [Name(Name)]
        [BaseDefinition("code")]
        internal static ContentTypeDefinition? PevtContentTypeDefinition;

        [Export]
        [FileExtension(FileExtension)]
        [ContentType(Name)]
        internal static FileExtensionToContentTypeDefinition? PevtFileExtensionDefinition;
#pragma warning restore 649
    }

    /// <summary>
    /// PEVT 专有的分类名。
    ///
    /// 只为"Visual Studio 没有对应概念"的四种记号新建分类：<c>@</c> 内置事件语句、<c>_</c> 自定义
    /// 事件块、标签，以及原始文本块内容。关键字、注释、字符串、数字、运算符一律复用
    /// <see cref="PredefinedClassificationTypeNames"/>——复用意味着用户的配色主题、"字体和颜色"
    /// 设置以及无障碍高对比度模式立刻就对 <c>.pevt</c> 生效，而自定义分类要用户自己配。
    /// </summary>
    internal static class PevtClassificationTypes
    {
        public const string BuiltinCall = "pevt.builtin-call";

        public const string BlockName = "pevt.block-name";

        public const string Label = "pevt.label";

        public const string RawContent = "pevt.raw-content";

#pragma warning disable 649
        [Export]
        [Name(BuiltinCall)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition? BuiltinCallDefinition;

        [Export]
        [Name(BlockName)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition? BlockNameDefinition;

        [Export]
        [Name(Label)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition? LabelDefinition;

        [Export]
        [Name(RawContent)]
        [BaseDefinition(PredefinedClassificationTypeNames.String)]
        internal static ClassificationTypeDefinition? RawContentDefinition;
#pragma warning restore 649
    }

    /// <summary>
    /// 四种 PEVT 专有分类的默认外观。
    ///
    /// 颜色只给默认值，不写死：<see cref="ClassificationFormatDefinition"/> 的取值会进入"字体和颜色"
    /// 设置页（<see cref="UserVisibleAttribute"/> 为 true），用户改过之后以用户的为准。
    /// </summary>
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PevtClassificationTypes.BuiltinCall)]
    [Name(PevtClassificationTypes.BuiltinCall)]
    [UserVisible(true)]
    [Order(After = Priority.Default)]
    internal sealed class PevtBuiltinCallFormat : ClassificationFormatDefinition
    {
        public PevtBuiltinCallFormat()
        {
            DisplayName = "PEVT 内置事件语句";
            ForegroundColor = Color.FromRgb(0x27, 0x5A, 0x8E);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PevtClassificationTypes.BlockName)]
    [Name(PevtClassificationTypes.BlockName)]
    [UserVisible(true)]
    [Order(After = Priority.Default)]
    internal sealed class PevtBlockNameFormat : ClassificationFormatDefinition
    {
        public PevtBlockNameFormat()
        {
            DisplayName = "PEVT 自定义事件块";
            ForegroundColor = Color.FromRgb(0x79, 0x5E, 0x26);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PevtClassificationTypes.Label)]
    [Name(PevtClassificationTypes.Label)]
    [UserVisible(true)]
    [Order(After = Priority.Default)]
    internal sealed class PevtLabelFormat : ClassificationFormatDefinition
    {
        public PevtLabelFormat()
        {
            DisplayName = "PEVT 标签";
            ForegroundColor = Color.FromRgb(0x6F, 0x42, 0x8A);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PevtClassificationTypes.RawContent)]
    [Name(PevtClassificationTypes.RawContent)]
    [UserVisible(true)]
    [Order(After = Priority.Default)]
    internal sealed class PevtRawContentFormat : ClassificationFormatDefinition
    {
        public PevtRawContentFormat()
        {
            DisplayName = "PEVT 原始文本块";
            ForegroundColor = Color.FromRgb(0x6A, 0x73, 0x7D);
        }
    }
}
