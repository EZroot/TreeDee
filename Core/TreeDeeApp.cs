using BepuPhysics;
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
using SDL2Engine.Core.Buffers.Interfaces;
using SDL2Engine.Core.Cameras;
using SDL2Engine.Core.Input;
using SDL2Engine.Core.Lighting;
using SDL2Engine.Core.Lighting.Interfaces;
using SDL2Engine.Core.Physics.Interfaces;
using SDL2Engine.Core.Utils;

namespace TreeDee.Core
{
    public class TreeDeeApp : IGame
    {
        IRenderService renderService;
        IWindowService windowService;
        IImageService imageService;
        IModelService modelService;
        private IPhysicsService physicsService;
        IFrameBufferService fboService;
        IGodRayBufferService grbService;
        ICameraService cameraService;
        IShadowPassService shadowPassService;

        nint[] texture;
        List<OpenGLHandle> assetHandles = new();
        List<BodyHandle> assetPhysicsHandles = new();

        // Grid movement.
        private Vector3 gridOffset = Vector3.Zero;
        private float gridSpeed = 0.1f;

        private OpenGLHandle quadHandle;
        private OpenGLHandle arrowHandle;

        int windowHeight = 1080, windowWidth = 1920;
        float totalTime = 0f;

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
            fboService = serviceProvider.GetService<IFrameBufferService>()
                         ?? throw new NullReferenceException(nameof(IFrameBufferService));
            grbService = serviceProvider.GetService<IGodRayBufferService>()
                         ?? throw new NullReferenceException(nameof(IGodRayBufferService));
            physicsService = serviceProvider.GetService<IPhysicsService>()
                         ?? throw new NullReferenceException(nameof(IPhysicsService));

            
            m_directionalLight = new Light(LightType.Directional, 50, 1, 150);
            shadowPassService.Initialize();

            // Load diffuse textures.
            texture = new nint[3];
            var tex = imageService.LoadTexture(TreeDeeHelper.RESOURCES_FOLDER + "/face.png");
            var tex1 = imageService.LoadTexture(
                TreeDeeHelper.RESOURCES_FOLDER + "/texture.jpg");
            var tex2 = imageService.LoadTexture(
                TreeDeeHelper.RESOURCES_FOLDER + "/3d/2k_mercury.jpg");
            texture[0] = tex.Texture;
            texture[2] = tex1.Texture;
            texture[1] = tex2.Texture;

            // shadow mapping shaders
            string sceneVertPath = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/shadows/shadow.vert";
            string sceneFragPath = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/shadows/shadow.frag";
            int totalCubes = cubeDim * cubeDim * cubeDim;
            for (int i = 0; i < totalCubes; i++)
            {
                var model = modelService.CreateSphere(sceneVertPath, sceneFragPath, 1920f / 1080f);
                assetHandles.Add(model);
                var pos = ComputeModelMatrixPos(model);
                var bodyHandle = physicsService.CreatePhysicsBody(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z),
                    new System.Numerics.Vector3(1f), 1f);
                assetPhysicsHandles.Add(bodyHandle);
                shadowPassService.RegisterMesh(model, MathHelper.GetMatrixTranslation(pos));
            }


            quadHandle = modelService.CreateQuad(sceneVertPath, sceneFragPath, 16f / 9f);
            shadowPassService.RegisterMesh(quadHandle, MathHelper.GetMatrixTranslation(Vector3.Zero));

            arrowHandle = modelService.Create3DArrow(
                TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/unlit/unlit.vert",
                TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/unlit/unlit.frag");
        }

        public void Update(float deltaTime)
        {
            totalTime += deltaTime;

            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_SPACE))
            {
                var handle = assetPhysicsHandles[0];
                var n = new System.Numerics.Vector3(0, 10, 0);
                physicsService.ApplyLinearImpulse(handle, in n);
            }
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
            // computing light / asset matrices
            Quaternion lightRotation = Quaternion.FromEulerAngles(lightRotationX, lightRotationY, lightRotationZ);
            var lightSpaceMatrix = m_directionalLight.Update(Vector3.Zero, lightRotation, 0);
            
            var floorPos = new Vector3(0, -20, -35);
            var floorModel = MathHelper.GetMatrixTranslation(floorPos, new Vector3(100, 100, 1)) *
                             MathHelper.GetMatrixRotationAroundPivot(0, 0, 0, -floorPos);
            var assetModels = new Matrix4[assetHandles.Count];
            

            for (int i = 0; i < assetHandles.Count; i++)
            {
                var bod = assetPhysicsHandles[i];
                var bodyRef = physicsService.GetBodyReference(bod);
                var bepuPos = bodyRef.Pose.Position;       
                var bepuRot = bodyRef.Pose.Orientation;   
                var pos = new OpenTK.Mathematics.Vector3(bepuPos.X, bepuPos.Y, bepuPos.Z);
                var rot = new OpenTK.Mathematics.Quaternion(bepuRot.X, bepuRot.Y, bepuRot.Z, bepuRot.W);
                Debug.Log($"BEPU: {pos} {rot}");
                Matrix4 rotationMatrix = Matrix4.CreateFromQuaternion(rot);
                Matrix4 translationMatrix = Matrix4.CreateTranslation(pos);
                Matrix4 modelMatrix = translationMatrix * rotationMatrix;
                assetModels[i] = modelMatrix;
                // assetModels[i] = ComputeModelMatrix(assetHandles[i]);
            }

            // shadow world space pass
            for (int i = 0; i < assetHandles.Count; i++)
            {
                shadowPassService.UpdateMeshModel(assetHandles[i], assetModels[i]);
            }
            shadowPassService.UpdateMeshModel(quadHandle, floorModel);

            shadowPassService.RenderShadowPass(m_directionalLight.LightView, m_directionalLight.LightProjection);

            // main scene geometry render
            fboService.BindFramebuffer(windowWidth, windowHeight);
            CameraGL3D cam = (CameraGL3D)cameraService.GetActiveCamera();
            for (int i = 0; i < assetHandles.Count; i++)
            {
                modelService.DrawModelGL(
                    assetHandles[i],
                    assetModels[i],
                    cam,
                    texture[1],
                    lightSpaceMatrix,
                    shadowPassService.DepthTexturePtr,
                    m_directionalLight.LightDirection,
                    new Vector3(1f, 1f, 1f),
                    Vector3.Zero);
            }

            modelService.DrawModelGL(quadHandle, floorModel, cam,
                texture[2], lightSpaceMatrix, shadowPassService.DepthTexturePtr,
                m_directionalLight.LightDirection, new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 0f));

            // light dir arrow
            modelService.DrawArrow(arrowHandle, cam, -m_directionalLight.LightPosition,
                m_directionalLight.LightDirection);
            
            shadowPassService.RenderDebugQuad(false, 1, 150);

            fboService.UnbindFramebuffer();

            // god ray world space render pass
            // grbService.BindFramebuffer(windowWidth, windowHeight);
            // for (int i = 0; i < assetHandles.Count; i++)
            // {
            //     modelService.DrawModelGL(
            //         assetHandles[i],
            //         assetModels[i],
            //         cam,
            //         texture[1],
            //         lightSpaceMatrix,
            //         shadowPassService.DepthTexturePtr,
            //         m_directionalLight.LightDirection,
            //         new Vector3(1f, 1f, 1f),
            //         Vector3.Zero);
            // }
            //
            // modelService.DrawModelGL(quadHandle, floorModel, cam,
            //     texture[2], lightSpaceMatrix, shadowPassService.DepthTexturePtr,
            //     m_directionalLight.LightDirection, new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 0f));
            // grbService.UnbindFramebuffer();
            //
            // // process god rays
            // grbService.ProcessGodRays(cam, (Light)m_directionalLight, fboService.GetDepthTexture());
            // grbService.RenderDebug(); // visualize god rays 
            GL.Viewport(0,0,windowWidth,windowHeight);
            Debug.Log($"{windowWidth} {windowHeight}");
            fboService.RenderFramebuffer();
        }

        public void RenderGui()
        {
        }

        public void Shutdown()
        {
        }

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
            Matrix4 translation = MathHelper.GetMatrixTranslation(pos, 5f);
            Matrix4 rotation = MathHelper.GetMatrixRotationAroundPivot(270, 0, totalTime * 10f, -pos);
            return translation * rotation;
        }
        
        private Vector3 ComputeModelMatrixPos(OpenGLHandle handle)
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
            return pos;
        }
    }
}