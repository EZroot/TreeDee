using SDL2Engine.Core;
using TreeDee.Core;

public static class Program
{
    public static void Main()
    {
        var app = new GameApp(RendererType.OpenGlRenderer, PipelineType.Pipe3D);
        app.Run(new TreeDeeApp());
    }
}