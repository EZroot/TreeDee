using Microsoft.Extensions.DependencyInjection;
using OpenTK.Graphics.ES11;
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
    private List<OpenGLHandle> m_assetHandles = new();
    public void Initialize(IServiceProvider serviceProvider)
    {
        m_renderService = serviceProvider.GetService<IRenderService>() ?? throw new NullReferenceException(nameof(IRenderService));
        m_windowService = serviceProvider.GetService<IWindowService>() ?? throw new NullReferenceException(nameof(IWindowService));
        m_imageService = serviceProvider.GetService<IImageService>() ?? throw new NullReferenceException(nameof(IImageService));
        m_cameraService = serviceProvider.GetService<ICameraService>() ?? throw new NullReferenceException(nameof(ICameraService));
        m_modelService = serviceProvider.GetService<IModelService>() ?? throw new NullReferenceException(nameof(IModelService));
        
        var tex = m_imageService.LoadTexture(m_renderService.RenderPtr, TreeDeeHelper.RESOURCES_FOLDER + "/texture.jpg");
        m_texture = tex.Texture;
        
        var model = m_modelService.Load3DModel(TreeDeeHelper.RESOURCES_FOLDER + "/3d/cat.obj", 
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert", 
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag",
            1f);
        
        var cube = m_modelService.LoadCube(
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert", 
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag",
            1f);
        
        var quad = m_modelService.LoadQuad(
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert", 
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag",
            1f);
        
        var sphere = m_modelService.LoadSphere(
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert", 
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag",
            1f);
        
        m_assetHandles.Add(model);
        m_assetHandles.Add(cube);
        m_assetHandles.Add(quad);
        m_assetHandles.Add(sphere);
    }
    public void Update(float deltaTime)
    {
        
    }

    public void Render()
    {
        for (var i = 0; i < m_assetHandles.Count; i++)
        {
            var z = 15;
            var y = 3;
            var xSpacer = 2.5f;
            if (i == 0) 
            { 
                z = 100;
                y = -40; 
            }
            var pos = new Vector3(0 + (i*xSpacer), y, z);
            var modelPos = MathHelper.GetMatrixTranslation(pos);
            var modelRotate = MathHelper.GetMatrixRotationAroundPivot(270f, 0, (float)Time.TotalTime * 100f, -pos);// * MathHelper.RotateZ((float)Time.TotalTime * 500f * Time.DeltaTime);
            var modelMatrix = modelPos * modelRotate;
            
            var asset = m_assetHandles[i];
            m_modelService.DrawModelGL(asset, modelMatrix, (CameraGL3D)m_cameraService.GetActiveCamera(), m_texture);
        }
    }

    public void RenderGui()
    {
        
    }

    public void Shutdown()
    {
        
    }
}