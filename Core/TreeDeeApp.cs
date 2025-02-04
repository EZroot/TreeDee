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
        int shadowWidth = 1024, shadowHeight = 1024;

        // Shaders.
        int depthShader;
        int debugShader;
        OpenGLHandle debugQuadHandle;

        int windowHeight = 1080, windowWidth = 1920;
        float totalTime = 0f;

        // Directional light settings.
        Vector3 lightDir = new Vector3(-0.2f, -1f, -0.3f);
        int lightProjSize = 50;
        float lightDistance = 55f;
        private Vector3 sceneCenter = -Vector3.UnitZ;

        // Cube grid.
        int cubeDim = 5;

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
                var sphere = modelService.LoadSphere(sceneVertPath, sceneFragPath, 16f / 9f);
                assetHandles.Add(sphere);
            }

            // Setup shadow framebuffer and depth texture.
            shadowFBO = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFBO);
            depthTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, depthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent,
                shadowWidth, shadowHeight, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
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

            float[] quadVertices = {
                -1f,  1f,  0f, 1f,
                -1f,  0f,  0f, 0f,
                 0f,  0f,  1f, 0f,
                -1f,  1f,  0f, 1f,
                 0f,  0f,  1f, 0f,
                 0f,  1f,  1f, 1f,
            };
            int quadVao = GL.GenVertexArray();
            int quadVbo = GL.GenBuffer();
            GL.BindVertexArray(quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.BindVertexArray(0);
            debugQuadHandle = new OpenGLHandle(new OpenGLMandatoryHandles(quadVao, quadVbo, 0, debugShader, 6));
        }

        public void Update(float deltaTime)
        {
            totalTime += deltaTime;

            // Grid movement.
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_w))
                gridOffset.Y += gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_s))
                gridOffset.Y -= gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_a))
                gridOffset.X -= gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_d))
                gridOffset.X += gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_z))
                gridOffset.Z -= gridSpeed;
            if (InputManager.IsKeyPressed(SDL.SDL_Keycode.SDLK_x))
                gridOffset.Z += gridSpeed;

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
            Vector3 normalizedDir = lightDir.Normalized();
            Vector3 target = sceneCenter + gridOffset;
            Vector3 lightPos = target - normalizedDir * lightDistance;
            Matrix4 lightView = Matrix4.LookAt(lightPos, target, Vector3.UnitY);

            float nearPlane = 0.02f, farPlane = 100f;
            Matrix4 lightProjection = Matrix4.CreateOrthographic(lightProjSize, lightProjSize, nearPlane, farPlane);
            Matrix4 lightSpaceMatrix = lightProjection * lightView;

            // PASS 1: Render scene depth from light's POV.
            GL.Viewport(0, 0, shadowWidth, shadowHeight);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFBO);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            GL.UseProgram(depthShader);
            GL.UniformMatrix4(GL.GetUniformLocation(depthShader, "lightView"), false, ref lightView);
            GL.UniformMatrix4(GL.GetUniformLocation(depthShader, "lightProjection"), false, ref lightProjection);
            foreach (var handle in assetHandles)
            {
                Matrix4 model = ComputeModelMatrix(handle);
                GL.UniformMatrix4(GL.GetUniformLocation(depthShader, "model"), false, ref model);
                GL.BindVertexArray(handle.Handles.Vao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, handle.Handles.VertexCount);
            }
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            // PASS 2: Render scene from camera using updated DrawModelGL.
            GL.Viewport(0, 0, windowWidth, windowHeight);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            CameraGL3D cam = (CameraGL3D)cameraService.GetActiveCamera();

            foreach (var handle in assetHandles)
            {
                Matrix4 model = ComputeModelMatrix(handle);
                modelService.DrawModelGL(handle, model, cam, texture[0], lightSpaceMatrix, depthTexture);
            }

            // Optional: Debug view of shadow map.
            GL.Viewport(0, 0, 1024, 1024);
            GL.UseProgram(debugShader);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, depthTexture);
            GL.Uniform1(GL.GetUniformLocation(debugShader, "debugTexture"), 0);
            GL.BindVertexArray(debugQuadHandle.Handles.Vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, debugQuadHandle.Handles.VertexCount);
            GL.BindVertexArray(0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.UseProgram(0);
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

            float spacing = 3f;
            float offset = (cubeDim - 1) * spacing * 0.5f;
            float rowOffset = (row % 2 == 1) ? spacing * 0.5f : 0f;
            Vector3 pos = new Vector3(col * spacing + rowOffset - offset,
                                      layer * spacing - offset,
                                      row * spacing - offset);
            pos += gridOffset;
            Matrix4 translation = Matrix4.CreateTranslation(pos);
            Matrix4 rotation = Matrix4.CreateRotationY(totalTime * Time.DeltaTime);
            return translation * rotation;
        }

        // // Updated DrawModelGL using new helper.
        // private void DrawModelGL(OpenGLHandle glHandle, Matrix4 modelMatrix, CameraGL3D camera,
        //     nint diffuseTexturePointer, Matrix4 lightSpaceMatrix, nint shadowMapPointer)
        // {
        //     int shader = glHandle.Handles.Shader;
        //     GL.UseProgram(shader);
        //
        //     // Set transformation matrices.
        //     int modelLoc = GL.GetUniformLocation(shader, "model");
        //     int viewLoc = GL.GetUniformLocation(shader, "view");
        //     int projLoc = GL.GetUniformLocation(shader, "projection");
        //     Matrix4 camView = camera.View;
        //     Matrix4 proj = camera.Projection;
        //     GL.UniformMatrix4(modelLoc, false, ref modelMatrix);
        //     GL.UniformMatrix4(viewLoc, false, ref camView);
        //     GL.UniformMatrix4(projLoc, false, ref proj);
        //
        //     // Set light space matrix for shadows.
        //     int lsLoc = GL.GetUniformLocation(shader, "lightSpaceMatrix");
        //     if (lsLoc != -1)
        //         GL.UniformMatrix4(lsLoc, false, ref lightSpaceMatrix);
        //
        //     // Bind diffuse texture to unit 0.
        //     GL.ActiveTexture(TextureUnit.Texture0);
        //     GL.BindTexture(TextureTarget.Texture2D, (int)diffuseTexturePointer);
        //     int diffuseLoc = GL.GetUniformLocation(shader, "diffuseTexture");
        //     if (diffuseLoc != -1)
        //         GL.Uniform1(diffuseLoc, 0);
        //
        //     // Bind shadow map to unit 1.
        //     GL.ActiveTexture(TextureUnit.Texture1);
        //     GL.BindTexture(TextureTarget.Texture2D, (int)shadowMapPointer);
        //     int shadowLoc = GL.GetUniformLocation(shader, "shadowMap");
        //     if (shadowLoc != -1)
        //         GL.Uniform1(shadowLoc, 1);
        //
        //     // Set lighting uniforms.
        //     Vector3 lightDirNorm = new Vector3(1f, 1f, 1f).Normalized();
        //     Vector3 lightColor = new Vector3(1f, 1f, 1f);
        //     Vector3 ambientColor = new Vector3(0f, 0f, 0f);
        //     int lightDirLoc = GL.GetUniformLocation(shader, "lightDir");
        //     int lightColorLoc = GL.GetUniformLocation(shader, "lightColor");
        //     int ambientColorLoc = GL.GetUniformLocation(shader, "ambientColor");
        //     if (lightDirLoc != -1)
        //         GL.Uniform3(lightDirLoc, ref lightDirNorm);
        //     if (lightColorLoc != -1)
        //         GL.Uniform3(lightColorLoc, ref lightColor);
        //     if (ambientColorLoc != -1)
        //         GL.Uniform3(ambientColorLoc, ref ambientColor);
        //
        //     GL.BindVertexArray(glHandle.Handles.Vao);
        //     GL.DrawArrays(PrimitiveType.Triangles, 0, glHandle.Handles.VertexCount);
        //     GL.BindVertexArray(0);
        //
        //     // Cleanup textures.
        //     GL.ActiveTexture(TextureUnit.Texture1);
        //     GL.BindTexture(TextureTarget.Texture2D, 0);
        //     GL.ActiveTexture(TextureUnit.Texture0);
        //     GL.BindTexture(TextureTarget.Texture2D, 0);
        //     GL.UseProgram(0);
        // }
        //
    }
}
