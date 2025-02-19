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
using SDL2Engine.Core.Geometry;
using SDL2Engine.Core.Input;
using SDL2Engine.Core.Lighting;
using SDL2Engine.Core.Lighting.Interfaces;
using SDL2Engine.Core.Physics.Interfaces;
using SDL2Engine.Core.Utils;
using SDL2Engine.Core.World;

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

        int windowHeight = 1080, windowWidth = 1920;
        private Scene m_scene;
        private GameObject3D m_arrowGO;
        
        private List<GameObject3D> m_physicsObjectList = new();

        public void Initialize(IServiceProvider serviceProvider)
        {
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

            EventHub.Subscribe<OnWindowResized>((sender, e) =>
            {
                windowHeight = e.WindowSettings.Height;
                windowWidth = e.WindowSettings.Width;
            });

            // New Scene
            m_scene = new Scene(serviceProvider.GetService<IFrameBufferService>(),
                serviceProvider.GetService<IShadowPassService>(), grbService, (CameraGL3D)cameraService.GetActiveCamera());

            // Textures
            var tex = imageService.LoadTexture(TreeDeeHelper.RESOURCES_FOLDER + "/face.png");
            var tex1 = imageService.LoadTexture(
                TreeDeeHelper.RESOURCES_FOLDER + "/texture.jpg");
            var tex2 = imageService.LoadTexture(
                TreeDeeHelper.RESOURCES_FOLDER + "/3d/EarthTextures/Diffuse_2K.png");

            // Shader setup
            string vertShader = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/shadows/shadow.vert";
            string fragShader = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/shadows/shadow.frag";
            var shader = new Shader(vertShader, fragShader);

            vertShader = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/unlit/unlit.vert";
            fragShader = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/unlit/unlit.frag";
            var shaderUnlit = new Shader(vertShader, fragShader);
            
            // Load mesh
            var model = modelService.CreateSphere();//LoadModel(PlatformInfo.RESOURCES_FOLDER + "/3d/Earth2K.obj");
            var model2 = modelService.LoadModel(PlatformInfo.RESOURCES_FOLDER + "/3d/cat.obj");
            var model3 = modelService.CreateQuad();
            var arrowMesh = modelService.CreateArrowMesh();

            // Create & Register gameobjects
            GameObject3D go = new GameObject3D(model, tex, shader);
            go.SetPosition(new Vector3(0, 0, 10));
            go.SetScale(new Vector3(2));
            go.SetCastShadows(true);
            m_scene.AddGameObject(go);

            GameObject3D go2 = new GameObject3D(model2, tex, shader);
            go2.SetPosition(new Vector3(10, 5, 10));
            go2.SetScale(new Vector3(1f));
            go2.SetCastShadows(true);
            m_scene.AddGameObject(go2);

            GameObject3D go3 = new GameObject3D(model3, tex, shader);
            go3.SetPosition(new Vector3(0, -30, 0));
            go3.SetScale(new Vector3(1) * 200);
            go3.SetRotation(Quaternion.FromEulerAngles(MathHelper.DegreesToRadians(90), MathHelper.DegreesToRadians(180), 0));
            // go3.SetCastShadows(true);
            m_scene.AddGameObject(go3);
            
            m_arrowGO = new GameObject3D(arrowMesh, tex, shader, Vector3.One, new Vector3(1,0,0));
            m_arrowGO.SetPosition(new Vector3(0, 0, 0));
            m_arrowGO.SetScale(new Vector3(1) * 10);
            m_arrowGO.SetRotation(Quaternion.FromEulerAngles(MathHelper.DegreesToRadians(90), 0, 0));
            m_scene.AddGameObject(m_arrowGO);
        
            // Ground
            physicsService.CreateStaticPhysicsBody(new System.Numerics.Vector3(0,-30,0), new System.Numerics.Vector3(400,1,400));
            for (var i = 0; i < 10; i++)
            {
                for (var j = 0; j < 10; j++)
                {
                    for (var k = 0; k < 10; k++)
                    {
                        var pos = new Vector3(j + 10, (i + j) * k, i + 10);
                        var physicsBody =
                            physicsService.CreateSpherePhysicsBody(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), 1f,
                                1f);
                        var physicsObject = new GameObject3D(model, tex, shader, physicsBody);
                        physicsObject.SetPosition(pos);
                        physicsObject.SetScale(new Vector3(1));
                        m_scene.AddGameObject(physicsObject);
                        m_physicsObjectList.Add(physicsObject);
                    }
                }
            }
        }

        private Vector3 lightRot = new Vector3(0, 0, 0);
        private Vector3 lightPos = new Vector3(0, -1, 0);
        public void Update(float deltaTime)
        {
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_w))
            {
                lightRot.Y += 10f * deltaTime;
            }
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_s))
            {
                lightRot.Y -= 10f * deltaTime;
            }
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_a))
            {
                lightRot.X -= 10f * deltaTime;
            }
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_d))
            {
                lightRot.X += 10f * deltaTime;
            }
            
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_i))
            {
                lightPos.Y += 10f * deltaTime;
            }
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_k))
            {
                lightPos.Y -= 10f * deltaTime;
            }
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_j))
            {
                lightPos.X -= 10f * deltaTime;
            }
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_l))
            {
                lightPos.X += 10f * deltaTime;
            }
            m_scene.DirectionalLight.Update(lightPos, Quaternion.FromEulerAngles(MathHelper.DegreesToRadians(lightRot.X), MathHelper.DegreesToRadians(lightRot.Y), 0), 0);
            m_arrowGO.SetPosition(lightPos);
            m_arrowGO.SetRotation(Quaternion.FromEulerAngles(MathHelper.DegreesToRadians(lightRot.X), MathHelper.DegreesToRadians(lightRot.Y), 0));

            foreach (var physicsObj in m_physicsObjectList)
            {
                var bodyRef = physicsService.GetBodyReference(physicsObj.Rigidbody.Value);
                var bepuPos = bodyRef.Pose.Position;
                var bepuRot = bodyRef.Pose.Orientation;
                var pos = new OpenTK.Mathematics.Vector3(bepuPos.X, bepuPos.Y, bepuPos.Z);
                var rot = new OpenTK.Mathematics.Quaternion(bepuRot.X, bepuRot.Y, bepuRot.Z, bepuRot.W);
                physicsObj.SetPosition(pos);
                physicsObj.SetRotation(rot);
            }
        }

        public void Render()
        {
            m_scene.Render();
        }

        public void RenderGui()
        {
        }

        public void Shutdown()
        {
        }
    }
}