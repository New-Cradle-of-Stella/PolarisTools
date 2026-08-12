using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace PolarisTools.Event.Language
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Comment)]
    [Name(HppClassificationTypeNames.Comment)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppCommentFormat : ClassificationFormatDefinition
    {
        public HppCommentFormat()
        {
            DisplayName = "Polaris Event (哈++) - Comment";
            ForegroundColor = Color.FromRgb(0x57, 0xA6, 0x4A);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Label)]
    [Name(HppClassificationTypeNames.Label)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppLabelFormat : ClassificationFormatDefinition
    {
        public HppLabelFormat()
        {
            DisplayName = "Polaris Event (哈++) - Label";
            ForegroundColor = Color.FromRgb(0xC5, 0x86, 0x00);
            IsBold = true;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Keyword)]
    [Name(HppClassificationTypeNames.Keyword)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppKeywordFormat : ClassificationFormatDefinition
    {
        public HppKeywordFormat()
        {
            DisplayName = "Polaris Event (哈++) - Command";
            ForegroundColor = Color.FromRgb(0x56, 0x9C, 0xD6);
            IsBold = true;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Actor)]
    [Name(HppClassificationTypeNames.Actor)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppActorFormat : ClassificationFormatDefinition
    {
        public HppActorFormat()
        {
            DisplayName = "Polaris Event (哈++) - Actor";
            ForegroundColor = Color.FromRgb(0x4E, 0xC9, 0xB0);
            IsBold = true;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Pose)]
    [Name(HppClassificationTypeNames.Pose)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppPoseFormat : ClassificationFormatDefinition
    {
        public HppPoseFormat()
        {
            DisplayName = "Polaris Event (哈++) - Pose";
            ForegroundColor = Color.FromRgb(0x4E, 0xC9, 0xB0);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.ParamName)]
    [Name(HppClassificationTypeNames.ParamName)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppParamNameFormat : ClassificationFormatDefinition
    {
        public HppParamNameFormat()
        {
            DisplayName = "Polaris Event (哈++) - Parameter Name";
            ForegroundColor = Color.FromRgb(0x9C, 0xDC, 0xFE);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Flag)]
    [Name(HppClassificationTypeNames.Flag)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppFlagFormat : ClassificationFormatDefinition
    {
        public HppFlagFormat()
        {
            DisplayName = "Polaris Event (哈++) - Flag";
            ForegroundColor = Color.FromRgb(0xC5, 0x86, 0xC0);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.String)]
    [Name(HppClassificationTypeNames.String)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppStringFormat : ClassificationFormatDefinition
    {
        public HppStringFormat()
        {
            DisplayName = "Polaris Event (哈++) - String";
            ForegroundColor = Color.FromRgb(0xD6, 0x9D, 0x85);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Number)]
    [Name(HppClassificationTypeNames.Number)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppNumberFormat : ClassificationFormatDefinition
    {
        public HppNumberFormat()
        {
            DisplayName = "Polaris Event (哈++) - Number";
            ForegroundColor = Color.FromRgb(0xB5, 0xCE, 0xA8);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = HppClassificationTypeNames.Variable)]
    [Name(HppClassificationTypeNames.Variable)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class HppVariableFormat : ClassificationFormatDefinition
    {
        public HppVariableFormat()
        {
            DisplayName = "Polaris Event (哈++) - Variable";
            ForegroundColor = Color.FromRgb(0xE0, 0x6C, 0x75);
            IsItalic = true;
        }
    }
}
