using Microsoft.Extensions.DependencyInjection;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SDL2Engine.Core;
using SDL2Engine.Core.Addressables.Interfaces;
using SDL2Engine.Core.Cameras.Interfaces;
using SDL2Engine.Core.Windowing.Interfaces;
using SDL2Engine.Events;
using TreeDee.Core.Utils;
using SDL2;
using SDL2Engine.Core.Addressables.Models.Interfaces;
using SDL2Engine.Core.Cameras;
using SDL2Engine.Core.Input;
using SDL2Engine.Core.Lighting;
using SDL2Engine.Core.Lighting.Interfaces;

namespace TreeDee.Core
{
    public class TreeDeeApp : IGame
    {
        IRenderService renderService;
        IWindowService windowService;
        IImageService imageService;
        IModelService modelService;
        ICameraService cameraService;
        IShadowPassService shadowPassService;
        
        nint[] texture;
        List<OpenGLHandle> assetHandles = new();

        // Grid movement.
        private Vector3 gridOffset = Vector3.Zero;
        private float gridSpeed = 0.1f;

        private OpenGLHandle quadHandle;
        private OpenGLHandle arrowHandle;

        int windowHeight = 1080, windowWidth = 1920;
        float totalTime = 0f;

        // Directional light settings.
        float lightDistance = 15f;

        // Cube grid.
        int cubeDim = 2;

        // Light rotation angles (in radians).
        float lightRotationX = 0f;
        float lightRotationY = 0f;
        float lightRotationZ = 0f;
        float rotationSpeed = 0.02f;

        private ILight m_directionalLight;

        public void Initialize(IServiceProvider serviceProvider)
        {
            EventHub.Subscribe<OnWindowResized>((sender, e) =>
            {
                windowHeight = e.WindowSettings.Height;
                windowWidth = e.WindowSettings.Width;
            });

            renderService = serviceProvider.GetService<IRenderService>()
                            ?? throw new NullReferenceException(nameof(IRenderService));
            windowService = serviceProvider.GetService<IWindowService>()
                            ?? throw new NullReferenceException(nameof(IWindowService));
            imageService = serviceProvider.GetService<IImageService>()
                           ?? throw new NullReferenceException(nameof(IImageService));
            cameraService = serviceProvider.GetService<ICameraService>()
                            ?? throw new NullReferenceException(nameof(ICameraService));
            modelService = serviceProvider.GetService<IModelService>()
                           ?? throw new NullReferenceException(nameof(IModelService));
            shadowPassService = serviceProvider.GetService<IShadowPassService>()
                               ?? throw new NullReferenceException(nameof(IShadowPassService));

            m_directionalLight = new Light(LightType.Directional, 50, 1, 150);
            shadowPassService.Initialize();
            
            // Load diffuse textures.
            texture = new nint[3];
            var tex = imageService.LoadTexture(renderService.RenderPtr, TreeDeeHelper.RESOURCES_FOLDER + "/face.png");
            var tex1 = imageService.LoadTexture(renderService.RenderPtr, TreeDeeHelper.RESOURCES_FOLDER + "/texture.jpg");
            var tex2 = imageService.LoadTexture(renderService.RenderPtr, TreeDeeHelper.RESOURCES_FOLDER + "/3d/Cat_diffuse.jpg");
            texture[0] = tex.Texture;
            texture[2] = tex1.Texture;
            texture[1] = tex2.Texture;

            // shadow mapping shaders
            string sceneVertPath = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/shadows/shadow.vert";
            string sceneFragPath = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/shadows/shadow.frag";
            int totalCubes = cubeDim * cubeDim * cubeDim;
            for (int i = 0; i < totalCubes; i++)
            {
                var model = modelService.Load3DModel(TreeDeeHelper.RESOURCES_FOLDER + "/3d/cat.obj", sceneVertPath, sceneFragPath, 16f / 9f);
                assetHandles.Add(model);
                shadowPassService.RegisterMesh(model, MathHelper.GetMatrixTranslation(Vector3.Zero));
            }

            quadHandle = modelService.CreateQuad(sceneVertPath, sceneFragPath, 16f / 9f);
            arrowHandle = modelService.Create3DArrow(
                TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/unlit/unlit.vert",
                TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/unlit/unlit.frag");
        }

        public void Update(float deltaTime)
        {
            totalTime += deltaTime;

            // Light distance adjustments.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_LSHIFT))
                lightDistance += 1f;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_LCTRL))
                lightDistance -= 1f;

            // --- Light Rotation Controls ---
            // Increase/decrease rotation around X-axis.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_i))
                lightRotationX += rotationSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_k))
                lightRotationX -= rotationSpeed;
            // Increase/decrease rotation around Y-axis.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_j))
                lightRotationY += rotationSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_l))
                lightRotationY -= rotationSpeed;
            // Increase/decrease rotation around Z-axis.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_u))
                lightRotationZ += rotationSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_o))
                lightRotationZ -= rotationSpeed;
        }

        public void Render()
        {
            // light matrices
            Quaternion lightRotation = Quaternion.FromEulerAngles(lightRotationX, lightRotationY, lightRotationZ);
            var lightSpaceMatrix = m_directionalLight.Update(new Vector3(0,0,0), lightRotation, lightDistance);

            // shadow pass
            var assetModels = new Matrix4[assetHandles.Count];
            for (var i = 0; i < assetHandles.Count; i++)
            {
                assetModels[i] = ComputeModelMatrix(assetHandles[i]);
                shadowPassService.UpdateMeshModel(assetHandles[i], assetModels[i]);
            }
            shadowPassService.RenderShadowPass( m_directionalLight.LightView, m_directionalLight.LightProjection);

            // main render pass
            modelService.BindFramebuffer(1920,1080);
            GL.CullFace(CullFaceMode.Back);
            var cam = (CameraGL3D)cameraService.GetActiveCamera();
            for (var i = 0; i < assetHandles.Count; i++)
            {
                modelService.DrawModelGL(
                    assetHandles[i], assetModels[i], cam, texture[1], lightSpaceMatrix, shadowPassService.DepthTexturePtr,
                    m_directionalLight.LightDirection, new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 0f));
            }

            var floorPos = new Vector3(0, -20, -15);
            var floorModel = MathHelper.GetMatrixTranslation(floorPos, new Vector3(100, 100, 100)) *
                             MathHelper.GetMatrixRotationAroundPivot(0, 0, 180, -floorPos);
            
            modelService.DrawModelGL(
                quadHandle, floorModel, cam, texture[2], lightSpaceMatrix, shadowPassService.DepthTexturePtr,
                m_directionalLight.LightDirection, new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 0f)
            );

            // Draw light direction arrow.
            modelService.DrawArrow(arrowHandle, cam, m_directionalLight.LightPosition, m_directionalLight.LightDirection);
            
            // shadowPassService.RenderDebugQuad();
            
            modelService.UnbindFramebuffer();
            
            // render frame buffer
            modelService.RenderFramebuffer();
        }

        public void RenderGui() { }

        public void Shutdown() { }

        // Computes the model matrix based on asset index and grid movement.
        private Matrix4 ComputeModelMatrix(OpenGLHandle handle)
        {
            int index = assetHandles.IndexOf(handle);
            int totalPerLayer = cubeDim * cubeDim;
            int layer = index / totalPerLayer;
            int rem = index % totalPerLayer;
            int row = rem / cubeDim;
            int col = rem % cubeDim;

            float spacing = 20f;
            float offset = (cubeDim - 1) * spacing * 0.5f;
            float rowOffset = (row % 2 == 1) ? spacing * 0.5f : 0f;
            Vector3 pos = new Vector3(col * spacing + rowOffset - offset,
                layer * spacing - offset,
                row * spacing - offset);
            pos += gridOffset;
            Matrix4 translation = MathHelper.GetMatrixTranslation(pos, .35f);
            Matrix4 rotation = MathHelper.GetMatrixRotationAroundPivot(270, 0, totalTime * 10f, -pos);
            return translation * rotation;
        }
    }
}
