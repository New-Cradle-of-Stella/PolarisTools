using Polaris.UI.Wire;
using System.Collections.Generic;

namespace PolarisTools.Pui.PuiVisualEditor.HotReload;

/// <summary>
/// <see cref="IPuiEmitter"/> 的热重载实现：不拼 C# 文本，而是原样收集
/// <see cref="PuiTreeWalker"/> 传来的每一次调用（连同它已经解析好的参数对象）成一份
/// <see cref="PuiWireCommand"/> 列表，稍后由 <see cref="PuiWireWriter"/> 序列化后
/// 通过命名管道推给正在运行的游戏进程。是否该发出哪个调用、参数怎么取默认值——
/// 这些判断已经在 <see cref="PuiTreeWalker"/> 里做完了，这里只是照单收集，
/// 游戏进程那侧的 PuiHotReloadBridge 同样只是照单执行，不重新理解 .pui 的语义。
/// </summary>
internal sealed class PuiHotReloadEmitter : IPuiEmitter
{
    public List<PuiWireCommand> Commands { get; } = new List<PuiWireCommand>();

    private void Add(PuiWireOpcode opcode, object payload) => Commands.Add(new PuiWireCommand { Opcode = opcode, Payload = payload });

    public void CreateWindow(string name, double pixelX, double pixelY, double width, double height, int appearDir, double appearLen, PuiMaskType mask)
        => Add(PuiWireOpcode.CreateWindow, new PuiCreateWindowParams
        {
            Name = name,
            PixelX = pixelX,
            PixelY = pixelY,
            Width = width,
            Height = height,
            AppearDir = appearDir,
            AppearLen = appearLen,
            Mask = mask,
        });

    public void SetFrameType(PuiFrameType frameType) => Add(PuiWireOpcode.SetFrameType, new PuiFrameTypeParams { FrameType = frameType });

    public void SetFocusable() => Add(PuiWireOpcode.SetFocusable, null);

    public void AddText(PuiTextParams p) => Add(PuiWireOpcode.AddText, p);

    public void AddButton(PuiButtonParams p) => Add(PuiWireOpcode.AddButton, p);

    public void AddSeparator(PuiSeparatorParams p) => Add(PuiWireOpcode.AddSeparator, p);

    public void Br() => Add(PuiWireOpcode.Br, null);

    public void SetLineAlign(PuiLineAlign align) => Add(PuiWireOpcode.SetLineAlign, new PuiLineAlignParams { Align = align });

    public void SetDefaultLineAlign() => Add(PuiWireOpcode.SetDefaultLineAlign, null);

    public void AddButtonMulti(PuiButtonMultiParams p) => Add(PuiWireOpcode.AddButtonMulti, p);

    public void AddChecks(PuiChecksParams p) => Add(PuiWireOpcode.AddChecks, p);

    public void AddRadio(PuiRadioParams p) => Add(PuiWireOpcode.AddRadio, p);

    public void AddSlider(PuiSliderParams p) => Add(PuiWireOpcode.AddSlider, p);

    public void AddInput(PuiInputParams p) => Add(PuiWireOpcode.AddInput, p);

    public void AddNumCounter(PuiNumCounterParams p) => Add(PuiWireOpcode.AddNumCounter, p);

    public void AddColorCell(PuiColorCellParams p) => Add(PuiWireOpcode.AddColorCell, p);

    public void AddImage(PuiImageParams p) => Add(PuiWireOpcode.AddImage, p);

    public void AddCustom(PuiCustomParams p) => Add(PuiWireOpcode.AddCustom, p);

    public void OnBuildCompleted(string methodName) => Add(PuiWireOpcode.OnBuildCompleted, new PuiMethodNameParams { MethodName = methodName });
}
