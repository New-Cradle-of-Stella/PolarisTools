using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 哈++（.phxx）语法高亮的分类类型名。范围对齐实现计划 §6.2：注释/标签/命令关键字/角色.姿势/参数名/
    /// 布尔标记/字符串/数值/变量插值。着色本身只依赖 <see cref="HppClassifier"/> 的逐行 lexer，不
    /// 依赖编译器的语义分析，保证输入时始终足够快。
    /// </summary>
    internal static class HppClassificationTypeNames
    {
        public const string Comment = "hpp.comment";
        public const string Label = "hpp.label";
        public const string Keyword = "hpp.keyword";
        public const string Actor = "hpp.actor";
        public const string Pose = "hpp.pose";
        public const string ParamName = "hpp.paramname";
        public const string Flag = "hpp.flag";
        public const string String = "hpp.string";
        public const string Number = "hpp.number";
        public const string Variable = "hpp.variable";
    }

    internal static class HppClassificationTypes
    {
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Comment)]
        internal static ClassificationTypeDefinition CommentType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Label)]
        internal static ClassificationTypeDefinition LabelType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Keyword)]
        internal static ClassificationTypeDefinition KeywordType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Actor)]
        internal static ClassificationTypeDefinition ActorType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Pose)]
        internal static ClassificationTypeDefinition PoseType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.ParamName)]
        internal static ClassificationTypeDefinition ParamNameType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Flag)]
        internal static ClassificationTypeDefinition FlagType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.String)]
        internal static ClassificationTypeDefinition StringType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Number)]
        internal static ClassificationTypeDefinition NumberType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(HppClassificationTypeNames.Variable)]
        internal static ClassificationTypeDefinition VariableType;
    }
}
