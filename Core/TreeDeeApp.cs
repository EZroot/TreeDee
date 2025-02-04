using Microsoft.Extensions.DependencyInjection;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SDL2Engine.Core;
using SDL2Engine.Core.Addressables.Interfaces;
using SDL2Engine.Core.Addressables.Models.Interfaces;
using SDL2Engine.Core.Rendering.Interfaces;
using SDL2Engine.Core.Utils;
using SDL2Engine.Core.Windowing.Interfaces;
using SDL2Engine.Events;
using TreeDee.Core.Utils;

namespace TreeDee.Core;

public class TreeDeeApp : IGame
{
    private IRenderService m_renderService;
    private IWindowService m_windowService;
    private IImageService m_imageService;
    private IModelService m_modelService;
    private ICameraService m_cameraService;

    private nint[] m_texture;
    private List<OpenGLHandle> m_assetHandles = new();

    private int shadowFBO, depthTexture;
    private int shadowWidth = 1024, shadowHeight = 1024;
    private int debugShader;
    private int depthShader;
    private OpenGLHandle debugQuadHandle;

    private int m_windowHeight = 1920, m_windowWidth = 1080;
    private float m_totalTime = 0f;

    private OpenGLHandle sphereHandle;

    public void Initialize(IServiceProvider serviceProvider)
    {
        EventHub.Subscribe<OnWindowResized>((sender, resized) =>
        {
            m_windowHeight = resized.WindowSettings.Height;
            m_windowWidth = resized.WindowSettings.Width;
        });

        m_renderService = serviceProvider.GetService<IRenderService>() ??
                          throw new NullReferenceException(nameof(IRenderService));
        m_windowService = serviceProvider.GetService<IWindowService>() ??
                          throw new NullReferenceException(nameof(IWindowService));
        m_imageService = serviceProvider.GetService<IImageService>() ??
                         throw new NullReferenceException(nameof(IImageService));
        m_cameraService = serviceProvider.GetService<ICameraService>() ??
                          throw new NullReferenceException(nameof(ICameraService));
        m_modelService = serviceProvider.GetService<IModelService>() ??
                         throw new NullReferenceException(nameof(IModelService));

        m_texture = new nint[4];
        var tex = m_imageService.LoadTexture(m_renderService.RenderPtr,
            TreeDeeHelper.RESOURCES_FOLDER + "/3d/Cat_diffuse.jpg");
        var texFace = m_imageService.LoadTexture(m_renderService.RenderPtr,
            TreeDeeHelper.RESOURCES_FOLDER + "/face.png");
        var texPinky = m_imageService.LoadTexture(m_renderService.RenderPtr,
            TreeDeeHelper.RESOURCES_FOLDER + "/pinkboysingle.png");
        var texCat = m_imageService.LoadTexture(m_renderService.RenderPtr,
            TreeDeeHelper.RESOURCES_FOLDER + "/texture.jpg");

        m_texture[0] = tex.Texture;
        m_texture[1] = texFace.Texture;
        m_texture[2] = texPinky.Texture;
        m_texture[3] = texCat.Texture;

        var model = m_modelService.Load3DModel(
            TreeDeeHelper.RESOURCES_FOLDER + "/3d/cat.obj",
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert",
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag", 1f);
        var cube = m_modelService.LoadCube(
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert",
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag", 1f);
        var quad = m_modelService.LoadQuad(
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert",
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag", 1f);
        var sphere = m_modelService.LoadSphere(
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert",
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag", 1f);

        m_assetHandles.Add(model);
        m_assetHandles.Add(cube);
        m_assetHandles.Add(quad);
        m_assetHandles.Add(sphere);
        
        // To represent the light
        sphereHandle = m_modelService.LoadSphere(
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.vert",
            TreeDeeHelper.RESOURCES_FOLDER + "/shaders/3d/3d.frag", 1f);

        // Create shadow FBO
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
        float[] borderColor = new float[] { 1f, 1f, 1f, 1f };
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, borderColor);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, depthTexture, 0);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            throw new Exception("Shadow framebuffer not complete!");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        // Create debug shader
        string debugVert = @"
#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;
out vec2 TexCoord;
void main(){ 
    gl_Position = vec4(aPos, 0.0, 1.0); 
    TexCoord = aTexCoord; 
}";
        string debugFrag = @"
#version 330 core
in vec2 TexCoord;
out vec4 FragColor;
uniform sampler2D debugTexture;
void main(){ 
    float depth = texture(debugTexture, TexCoord).r;
    FragColor = vec4(vec3(depth), 1.0);
}";
        debugShader = GLHelper.CreateShaderProgram(debugVert, debugFrag);

        // Create screen-space quad for debugging
        float[] quadVertices = {
            // positions   // texCoords
            -1f,  1f,      0f, 1f,
            -1f,  0f,      0f, 0f,
             0f,  0f,      1f, 0f,
            -1f,  1f,      0f, 1f,
             0f,  0f,      1f, 0f,
             0f,  1f,      1f, 1f,
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

        // Create a simple depth-only shader
        string depthVert = @"
#version 330 core
layout (location = 0) in vec3 aPos;
uniform mat4 model;
uniform mat4 lightView;
uniform mat4 lightProjection;
void main(){
    gl_Position = lightProjection * lightView * model * vec4(aPos, 1.0);
}";
        string depthFrag = @"
#version 330 core
void main(){ }";
        depthShader = GLHelper.CreateShaderProgram(depthVert, depthFrag);
    }

    public void Update(float deltaTime)
    {
        m_totalTime += deltaTime;
    }

    public void Render()
    {
        // PASS 1: Render scene from light's POV into depth texture
        GL.Viewport(0, 0, shadowWidth, shadowHeight);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFBO);
        GL.Clear(ClearBufferMask.DepthBufferBit);

        Vector3 lightPos = new Vector3(0f, 0f, 5);
        Matrix4 lightView = Matrix4.LookAt(lightPos, Vector3.Zero, Vector3.UnitY);

        Matrix4 lightProjection = Matrix4.CreateOrthographic(80, 80, 1, 70);

        GL.UseProgram(depthShader);
        int modelLoc = GL.GetUniformLocation(depthShader, "model");
        int lightViewLoc = GL.GetUniformLocation(depthShader, "lightView");
        int lightProjLoc = GL.GetUniformLocation(depthShader, "lightProjection");
        GL.UniformMatrix4(lightViewLoc, false, ref lightView);
        GL.UniformMatrix4(lightProjLoc, false, ref lightProjection);

        for (int i = 0; i < m_assetHandles.Count; i++)
        {
            float z = (i == 0) ? -30f : -25f;
            float y = (i == 0) ? 0 : 0;
            float x = -15;
            float xSpacer = 10.5f;
            Vector3 pos = new Vector3(x + i * xSpacer, y, z);
            Matrix4 modelMatrix = ComputeModelMatrix(i, pos);
            GL.UniformMatrix4(modelLoc, false, ref modelMatrix);
            GL.BindVertexArray(m_assetHandles[i].Handles.Vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, m_assetHandles[i].Handles.VertexCount);
        }
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        GL.Viewport(0, 0, m_windowWidth, m_windowHeight);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        
        m_modelService.DrawModelGL(sphereHandle, MathHelper.GetMatrixTranslation(lightPos,5f),
            (CameraGL3D)m_cameraService.GetActiveCamera(), m_texture[3]);
        
        for (int i = 0; i < m_assetHandles.Count; i++)
        {
            float z = (i == 0) ? -30f : -25f;
            float y = (i == 0) ? 0 : 0;
            float x = -15;
            float xSpacer = 10.5f;
            Vector3 pos = new Vector3(x + i * xSpacer, y, z);
            Matrix4 modelMatrix = ComputeModelMatrix(i, pos);
            m_modelService.DrawModelGL(m_assetHandles[i], modelMatrix,
                (CameraGL3D)m_cameraService.GetActiveCamera(), m_texture[i]);
        }

        // Debug: Render the shadow map on a quad
        GL.Viewport(0, 0, 1024, 1024);
        GL.UseProgram(debugShader);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, depthTexture);
        int texLoc = GL.GetUniformLocation(debugShader, "debugTexture");
        GL.Uniform1(texLoc, 0);
        GL.BindVertexArray(debugQuadHandle.Handles.Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, debugQuadHandle.Handles.VertexCount);
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
    }

    public void RenderGui() { }

    public void Shutdown() { }

    private Matrix4 ComputeModelMatrix(int index, Vector3 pos)
    {
        Matrix4 modelPos = MathHelper.GetMatrixTranslation(pos, index == 0 ? 0.25f : 1f);
        float originalRot = (index == 0) ? 270f : 0f;
        Matrix4 modelRotate;
        if (index == 0)
            modelRotate = MathHelper.GetMatrixRotationAroundPivot(originalRot, 0f, m_totalTime * 100f * Time.DeltaTime, -pos);
        else
            modelRotate = MathHelper.GetMatrixRotationAroundPivot(originalRot, m_totalTime * 100f * Time.DeltaTime, 0f, -pos);
        return modelPos * modelRotate;
    }
}
