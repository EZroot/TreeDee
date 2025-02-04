using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SDL2Engine.Core;
using SDL2Engine.Core.Addressables.Interfaces;
using SDL2Engine.Core.Rendering.Interfaces;
using SDL2Engine.Core.Utils;
using SDL2Engine.Core.Windowing.Interfaces;
using SDL2Engine.Events;
using TreeDee.Core.Utils;
using SDL2;
using SDL2Engine.Core.Addressables.Models.Interfaces;
using SDL2Engine.Core.Input;

namespace TreeDee.Core
{
    public class TreeDeeApp : IGame
    {
        IRenderService renderService;
        IWindowService windowService;
        IImageService imageService;
        IModelService modelService;
        ICameraService cameraService;

        nint[] texture;
        List<OpenGLHandle> assetHandles = new();

        // Grid movement.
        private Vector3 gridOffset = Vector3.Zero;
        private float gridSpeed = 0.1f;

        // Shadow mapping.
        int shadowFBO, depthTexture;
        int shadowWidth = 512, shadowHeight = 512;

        // Shaders.
        int depthShader;
        int debugShader;
        OpenGLHandle debugQuadHandle;
        private OpenGLHandle quadHandle;

        // New arrow handle/shader for visualizing the light direction:
        private OpenGLHandle arrowHandle;
        private int arrowShader;

        int windowHeight = 1080, windowWidth = 1920;
        float totalTime = 0f;

        // Directional light settings.
        Vector3 lightDir = new Vector3(20,20,20);
        int lightProjSize = 50;
        float lightDistance = 15f;
        private Vector3 sceneCenter = new Vector3(0, 0, 0);

        // Cube grid.
        int cubeDim = 1;

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

            // Load diffuse texture.
            texture = new nint[1];
            var tex = imageService.LoadTexture(renderService.RenderPtr, TreeDeeHelper.RESOURCES_FOLDER + "/face.png");
            texture[0] = tex.Texture;

            // Use updated LoadSphere that supports shadow mapping uniforms.
            string sceneVertPath = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/scene/SceneShadow.vert";
            string sceneFragPath = TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/scene/SceneShadow.frag";
            int totalCubes = cubeDim * cubeDim * cubeDim;
            for (int i = 0; i < totalCubes; i++)
            {
                var sphere = modelService.Load3DModel(TreeDeeHelper.RESOURCES_FOLDER + "/3d/cat.obj", sceneVertPath,
                    sceneFragPath, 16f / 9f);//LoadSphere(sceneVertPath, sceneFragPath, 16f / 9f);
                assetHandles.Add(sphere);
            }

            quadHandle = modelService.LoadQuad(sceneVertPath, sceneFragPath, 16f / 9f);

            // Setup shadow framebuffer and depth texture.
            shadowFBO = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFBO);
            depthTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, depthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent,
                shadowWidth, shadowHeight, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToBorder);
            float[] borderColor = { 1f, 1f, 1f, 1f };
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, borderColor);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, depthTexture, 0);
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new Exception("Shadow framebuffer incomplete!");
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            // Create depth shader.
            string depthVert = @"
                #version 330 core
                layout (location = 0) in vec3 aPos;
                uniform mat4 model;
                uniform mat4 lightView;
                uniform mat4 lightProjection;
                void main()
                {
                    gl_Position = lightProjection * lightView * model * vec4(aPos, 1.0);
                }";
            string depthFrag = @"
                #version 330 core
                void main() { }";
            depthShader = GLHelper.CreateShaderProgram(depthVert, depthFrag);

            // Debug shader for visualizing the depth (shadow) map.
            string debugVert = @"
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec2 aTexCoord;
                out vec2 TexCoord;
                void main() {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    TexCoord = aTexCoord;
                }";
            string debugFrag = @"
                #version 330 core
                in vec2 TexCoord;
                out vec4 FragColor;
                uniform sampler2D debugTexture;
                void main() {
                    float depth = texture(debugTexture, TexCoord).r;
                    FragColor = vec4(vec3(depth), 1.0);
                }";
            debugShader = GLHelper.CreateShaderProgram(debugVert, debugFrag);

            // Create a tiny fullscreen debug quad handle.
            float[] quadVertices =
            {
                -1f, 1f, 0f, 1f,
                -1f, 0f, 0f, 0f,
                0f, 0f, 1f, 0f,
                -1f, 1f, 0f, 1f,
                0f, 0f, 1f, 0f,
                0f, 1f, 1f, 1f,
            };
            int quadVao = GL.GenVertexArray();
            int quadVbo = GL.GenBuffer();
            GL.BindVertexArray(quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices,
                BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.BindVertexArray(0);
            debugQuadHandle = new OpenGLHandle(new OpenGLMandatoryHandles(quadVao, quadVbo, 0, debugShader, 6));

            arrowShader = CreateUnlitShader(); 
            float[] arrowVerts = GenerateBetterArrowGeometry();

            int arrowVao = GL.GenVertexArray();
            int arrowVbo = GL.GenBuffer();
            GL.BindVertexArray(arrowVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, arrowVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, arrowVerts.Length * sizeof(float),
                arrowVerts, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            GL.BindVertexArray(0);

            int arrowVertCount = arrowVerts.Length / 3;

            arrowHandle = new OpenGLHandle(
                new OpenGLMandatoryHandles(arrowVao, arrowVbo, 0, arrowShader, arrowVertCount)
            );

        }

        public void Update(float deltaTime)
        {
            totalTime += deltaTime;

            // Grid movement.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_w))
                lightDir.Y += gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_s))
                lightDir.Y -= gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_a))
                lightDir.X -= gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_d))
                lightDir.X += gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_z))
                lightDir.Z -= gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_x))
                lightDir.Z += gridSpeed;

            // Light distance adjustments.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_LSHIFT))
                lightDistance += 1f;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_LCTRL))
                lightDistance -= 1f;

            // Light projection size adjustments.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_t))
                lightProjSize += 1;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_r))
                lightProjSize -= 1;
        }
public void Render()
{
    // --- 1) Compute Light Matrices ---
    var dir = lightDir.Normalized();
    var lightPos = sceneCenter - dir * lightDistance;
    var lightView = Matrix4.LookAt(lightPos, sceneCenter, Vector3.UnitY);
    var lightProjection = Matrix4.CreateOrthographicOffCenter(
        -lightProjSize, lightProjSize,
        -lightProjSize, lightProjSize,
        0.1f, 100f
    );
    var lightSpaceMatrix = lightProjection * lightView;

    // --- 2) Shadow Pass: Render depth from light POV ---
    GL.Enable(EnableCap.CullFace);
    GL.CullFace(CullFaceMode.Back);
    GL.Viewport(0, 0, shadowWidth, shadowHeight);
    GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFBO);
    GL.Clear(ClearBufferMask.DepthBufferBit);

    GL.UseProgram(depthShader);
    GL.UniformMatrix4(GL.GetUniformLocation(depthShader, "lightView"), false, ref lightView);
    GL.UniformMatrix4(GL.GetUniformLocation(depthShader, "lightProjection"), false, ref lightProjection);

    GL.Enable(EnableCap.PolygonOffsetFill);
    GL.Enable(EnableCap.Blend);
    GL.BlendEquation(BlendEquationMode.Min);
    GL.PolygonOffset(1.1f, 1.0f);

    // Render all scene objects for shadow
    foreach (var handle in assetHandles)
    {
        var model = ComputeModelMatrix(handle);
        GL.UniformMatrix4(GL.GetUniformLocation(depthShader, "model"), false, ref model);
        GL.BindVertexArray(handle.Handles.Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, handle.Handles.VertexCount);
    }

    // Ground quad (or whatever floor)
    var floorPos = new Vector3(0, -20, -15);
    var floorModel = MathHelper.GetMatrixTranslation(floorPos, new Vector3(100, 100, 100))
                     * MathHelper.GetMatrixRotationAroundPivot(0, 0, 180, -floorPos);
    GL.UniformMatrix4(GL.GetUniformLocation(depthShader, "model"), false, ref floorModel);
    GL.BindVertexArray(quadHandle.Handles.Vao);
    GL.DrawArrays(PrimitiveType.Triangles, 0, quadHandle.Handles.VertexCount);

    GL.Disable(EnableCap.Blend);
    GL.Disable(EnableCap.PolygonOffsetFill);
    GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

    // --- 3) Main Pass: Render from camera perspective ---
    GL.Viewport(0, 0, windowWidth, windowHeight);
    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    var cam = (CameraGL3D)cameraService.GetActiveCamera();

    // Draw objects with shadows
    foreach (var handle in assetHandles)
    {
        var model = ComputeModelMatrix(handle);
        modelService.DrawModelGL(
            handle, model, cam, texture[0], lightSpaceMatrix, depthTexture,
            lightDir, new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 0.1f)
        );
    }

    // Draw ground quad
    modelService.DrawModelGL(
        quadHandle, floorModel, cam, texture[0], lightSpaceMatrix, depthTexture,
        lightDir, new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 0.1f)
    );

    // Draw arrow showing light direction
    DrawLightArrow(cam, lightPos, dir * lightDistance);

    // --- 4) Debug: Visualize Shadow Map ---
    GL.Viewport(0, 0, 1024, 1024);
    GL.UseProgram(debugShader);
    GL.ActiveTexture(TextureUnit.Texture0);
    GL.BindTexture(TextureTarget.Texture2D, depthTexture);
    GL.Uniform1(GL.GetUniformLocation(debugShader, "debugTexture"), 0);

    GL.BindVertexArray(debugQuadHandle.Handles.Vao);
    GL.DrawArrays(PrimitiveType.Triangles, 0, debugQuadHandle.Handles.VertexCount);

    // Cleanup
    GL.BindVertexArray(0);
    GL.BindTexture(TextureTarget.Texture2D, 0);
    GL.UseProgram(0);
    GL.Disable(EnableCap.CullFace);
}

        public void RenderGui()
        {
        }

        public void Shutdown()
        {
        }

        private void DrawLightArrow(CameraGL3D cam, Vector3 lightPos, Vector3 lightDirNormalized)
        {
            // Arrow is modeled along +Z
            Vector3 forward = -lightDirNormalized;
            Vector3 zAxis = Vector3.UnitZ;
            // Cross/dot for orientation.
            Vector3 axis = Vector3.Cross(zAxis, forward);
            float dot = MathF.Max(-1f, MathF.Min(1f, Vector3.Dot(zAxis, forward)));
            float angle = MathF.Acos(dot);

            Quaternion orientation = Quaternion.FromAxisAngle(axis.Normalized(), angle);

            // Scale, then translate to light position
            Matrix4 arrowModel =
                Matrix4.CreateFromQuaternion(orientation) *
                Matrix4.CreateScale(0.5f) *
                Matrix4.CreateTranslation(lightPos);

            // Use unlit arrow shader
            GL.UseProgram(arrowShader);

            // Send transforms
            int modelLoc = GL.GetUniformLocation(arrowShader, "uModel");
            int viewLoc = GL.GetUniformLocation(arrowShader, "uView");
            int projLoc = GL.GetUniformLocation(arrowShader, "uProjection");

            GL.UniformMatrix4(modelLoc, false, ref arrowModel);

            Matrix4 view = cam.View;
            GL.UniformMatrix4(viewLoc, false, ref view);

            Matrix4 proj = cam.Projection;
            GL.UniformMatrix4(projLoc, false, ref proj);

            // Optional arrow color
            int colorLoc = GL.GetUniformLocation(arrowShader, "uColor");
            if (colorLoc != -1)
            {
                Vector3 arrowColor = new Vector3(1f, 0f, 0f);
                GL.Uniform3(colorLoc, ref arrowColor);
            }

            // Draw arrow geometry
            GL.BindVertexArray(arrowHandle.Handles.Vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, arrowHandle.Handles.VertexCount);
            GL.BindVertexArray(0);

            GL.UseProgram(0);
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

            float spacing = 3f;
            float offset = (cubeDim - 1) * spacing * 0.5f;
            float rowOffset = (row % 2 == 1) ? spacing * 0.5f : 0f;
            Vector3 pos = new Vector3(col * spacing + rowOffset - offset,
                layer * spacing - offset,
                row * spacing - offset);
            pos += gridOffset;
            Matrix4 translation = Matrix4.CreateTranslation(pos);
            Matrix4 rotation = MathHelper.GetMatrixRotationAroundPivot(270, 0, totalTime * 10f, -pos);//Matrix4.CreateRotationY(totalTime * Time.DeltaTime));
            return translation * rotation;
        }

        private float[] GenerateBetterArrowGeometry(float shaftRadius = 0.02f,
            float shaftLength = 0.7f,
            float tipRadius = 0.06f,
            float tipLength = 0.3f,
            int segments = 16)
        {
            // We build a cylinder (for the shaft) from z=0 to z=shaftLength,
            // then a cone from z=shaftLength to z=shaftLength + tipLength.

            // We'll create a ring of vertices for both top and bottom of the shaft,
            // then a ring + tip for the cone.
            // For unlit rendering

            List<float> verts = new List<float>();

            //    - base circle at z=0, top circle at z=shaftLength
            //    - We'll form triangle strips around the circumference
            for (int i = 0; i < segments; i++)
            {
                float theta = 2f * MathF.PI * (i / (float)segments);
                float nextTheta = 2f * MathF.PI * ((i + 1) % segments / (float)segments);

                // base circle points (z=0)
                Vector3 p0 = new Vector3(shaftRadius * MathF.Cos(theta),
                    shaftRadius * MathF.Sin(theta),
                    0f);
                Vector3 p1 = new Vector3(shaftRadius * MathF.Cos(nextTheta),
                    shaftRadius * MathF.Sin(nextTheta),
                    0f);

                // top circle points (z=shaftLength)
                Vector3 p2 = p0 + new Vector3(0, 0, shaftLength);
                Vector3 p3 = p1 + new Vector3(0, 0, shaftLength);

                // two triangles forming a quad side
                // triangle1: p0, p2, p1
                verts.AddRange(new float[]
                {
                    p0.X, p0.Y, p0.Z,
                    p2.X, p2.Y, p2.Z,
                    p1.X, p1.Y, p1.Z
                });

                // triangle2: p1, p2, p3
                verts.AddRange(new float[]
                {
                    p1.X, p1.Y, p1.Z,
                    p2.X, p2.Y, p2.Z,
                    p3.X, p3.Y, p3.Z
                });
            }

            //    We'll add a triangle fan for the top circle at z=shaftLength.
            //    The center is at (0,0,shaftLength).
            Vector3 shaftTopCenter = new Vector3(0, 0, shaftLength);
            for (int i = 0; i < segments; i++)
            {
                float theta = 2f * MathF.PI * (i / (float)segments);
                float nextTheta = 2f * MathF.PI * ((i + 1) % segments / (float)segments);

                Vector3 p0 = new Vector3(shaftRadius * MathF.Cos(theta),
                    shaftRadius * MathF.Sin(theta),
                    shaftLength);
                Vector3 p1 = new Vector3(shaftRadius * MathF.Cos(nextTheta),
                    shaftRadius * MathF.Sin(nextTheta),
                    shaftLength);

                verts.AddRange(new float[]
                {
                    shaftTopCenter.X, shaftTopCenter.Y, shaftTopCenter.Z,
                    p0.X, p0.Y, p0.Z,
                    p1.X, p1.Y, p1.Z
                });
            }

            //    - base circle at z=shaftLength, apex at z=shaftLength + tipLength
            float tipZStart = shaftLength;
            float tipZEnd = shaftLength + tipLength;

            Vector3 tipApex = new Vector3(0, 0, tipZEnd);

            for (int i = 0; i < segments; i++)
            {
                float theta = 2f * MathF.PI * (i / (float)segments);
                float nextTheta = 2f * MathF.PI * ((i + 1) % segments / (float)segments);

                // ring base circle
                Vector3 b0 = new Vector3(tipRadius * MathF.Cos(theta),
                    tipRadius * MathF.Sin(theta),
                    tipZStart);
                Vector3 b1 = new Vector3(tipRadius * MathF.Cos(nextTheta),
                    tipRadius * MathF.Sin(nextTheta),
                    tipZStart);

                // single triangle to apex
                verts.AddRange(new float[]
                {
                    b0.X, b0.Y, b0.Z,
                    tipApex.X, tipApex.Y, tipApex.Z,
                    b1.X, b1.Y, b1.Z
                });
            }

            return verts.ToArray();
        }


        // Creates a basic unlit shader for the arrow
        private int CreateUnlitShader()
        {
            string vs = @"
                #version 330 core
                layout(location = 0) in vec3 aPos;
                uniform mat4 uModel;
                uniform mat4 uView;
                uniform mat4 uProjection;
                void main()
                {
                    gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
                }";

            string fs = @"
                #version 330 core
                out vec4 FragColor;
                uniform vec3 uColor;
                void main()
                {
                    FragColor = vec4(uColor, 1.0);
                }";

            return GLHelper.CreateShaderProgram(vs, fs);
        }
    }
}