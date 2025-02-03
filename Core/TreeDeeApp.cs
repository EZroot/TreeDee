using Microsoft.Extensions.DependencyInjection;
using OpenTK.Mathematics;
using SDL2Engine.Core;
using SDL2Engine.Core.Addressables.Interfaces;
using SDL2Engine.Core.Addressables.Models.Interfaces;
using SDL2Engine.Core.Rendering.Interfaces;
using SDL2Engine.Core.Windowing.Interfaces;
using TreeDee.Core.Utils;

namespace TreeDee.Core;

public class TreeDeeApp : IGame
{
    private IRenderService m_renderService;
    private IWindowService m_windowService;
    private IImageService m_imageService;
    private IModelService m_modelService;
    private ICameraService m_cameraService;

    private nint m_texture;
    private OpenGLHandle _handle;
    public void Initialize(IServiceProvider serviceProvider)
    {
        m_renderService = serviceProvider.GetService<IRenderService>() ?? throw new NullReferenceException(nameof(IRenderService));
        m_windowService = serviceProvider.GetService<IWindowService>() ?? throw new NullReferenceException(nameof(IWindowService));
        m_imageService = serviceProvider.GetService<IImageService>() ?? throw new NullReferenceException(nameof(IImageService));
        m_cameraService = serviceProvider.GetService<ICameraService>() ?? throw new NullReferenceException(nameof(ICameraService));
        m_modelService = serviceProvider.GetService<IModelService>() ?? throw new NullReferenceException(nameof(IModelService));
        
        var tex = m_imageService.LoadTexture(m_renderService.RenderPtr, TreeDeeHelper.RESOURCES_FOLDER + "/texture.jpg");
        m_texture = tex.Texture;
        
        _handle = m_modelService.Load3DModel(TreeDeeHelper.RESOURCES_FOLDER + "/3d/cat.obj", 
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert", 
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag",
            1f);
    }
    public void Update(float deltaTime)
    {
        
    }

    public void Render()
    {
        var pos = new Vector3(0, -40, 100);
        var modelPos = MathHelper.GetMatrixTranslation(pos);
        var modelRotate = MathHelper.GetMatrixRotationAroundPivot(270f, 0, (float)Time.TotalTime * 100f, -pos);// * MathHelper.RotateZ((float)Time.TotalTime * 500f * Time.DeltaTime);
        var modelMatrix = modelPos * modelRotate;
        m_modelService.DrawModelGL(_handle, modelMatrix, (CameraGL3D)m_cameraService.GetActiveCamera(), m_texture);
        m_imageService.DrawCubeGL(m_renderService.OpenGLHandle3D, 
            MathHelper.GetMatrixTranslation(new Vector3(0,2,5)),
            (CameraGL3D)m_cameraService.GetActiveCamera(),
            m_texture);
    }

    public void RenderGui()
    {
        
    }

    public void Shutdown()
    {
        
    }
}