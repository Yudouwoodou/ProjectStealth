﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Rendering script.
// Handles shadowmapping and post-processing effects.
// (Desaturation from adrenaline mode, night vision goggle effect)
// - Gabriel Violette

//---------------------------
// Note on render depths (Z):
//  camera          = -10
//  character       =  -5
//  geometry        =   0
//  shadow          =   0.5
//  near background =   1,   range: ( 0.01, 1.99 )
//  far background is rendered first, so z doesn't really matter (except for parallax layering).
//----------------------------------------------------------------------------------------------
[ExecuteInEditMode]
public class RenderEffects : MonoBehaviour
{
    #region vars
    #region desaturate
    [Range(0,1)]
    public float desaturation = 1.0f;
    [SerializeField]
    private Shader desaturate_shader;
    private Material desaturate_material;
    #endregion

    #region NVG effect
    public bool is_night_vision_on = true;
    [SerializeField]
    private Shader NVG_shader;
    private Material NVG_material;
    #endregion

    #region shadow mapping
    private CommandBuffer command_buffer;
    [SerializeField]
    private RenderTexture raw_shadow_map;
    [SerializeField]
    private RenderTexture shadow_map;
    [SerializeField]
    private Shader raw_shadow_map_shader;
    [SerializeField]
    private Shader shadow_map_shader;
    private Material raw_shadow_map_material;
    private Material shadow_map_material;
    [SerializeField]
    private Shader shadow_shader;
    [SerializeField]
    private RenderTexture shadow;
    private GameObject shadow_object;          // object under lighting, which renders the shadows

    private ulong allocated_shadow_maps = 0;   // mask representing 64 bools (64 x 1/0 bits) (FULL)
    public const int MAX_SHADOW_MAPS = 64;     // this needs to match the height of the shadow map render textures.
    private GameObject top_level_object;       // object at top of level hierarchy
    #endregion
    #endregion

    // Use for pre-initialization initialization.
    private void Awake()
    {
        // Construct effect materials
        desaturate_material = new Material( desaturate_shader );
        NVG_material = new Material( NVG_shader );

        // Construct shadow map materials and rendertexture
        if ( raw_shadow_map_material == null ) { raw_shadow_map_material = new Material( raw_shadow_map_shader ); }
        if ( shadow_map_material == null )     { shadow_map_material = new Material( shadow_map_shader ); }
        shadow = new RenderTexture( Screen.width, Screen.height, 0 ); // TODO: on resize, change

        // Get the object at the top of the level hierarchy.
        // We use it to fish for all lighting and occlusion for shadow mapping later on.
        top_level_object = GameObject.Find( "Level" );
        if ( top_level_object == null )
        {
            #if UNITY_EDITOR
            Debug.LogError( "Scene build error: missing object named Level" );
            #endif
        }

        // Get the scene's shadow renderer object.
        shadow_object = GameObject.Find( "Shadow" );
        if ( shadow_object == null )
        {
            #if UNITY_EDITOR
            Debug.LogError( "Scene build error: missing object named Shadow" );
            #endif
        }

        // Command buffer initialization
        if ( command_buffer == null )
        {
            command_buffer = new CommandBuffer();
            GetComponent<Camera>().AddCommandBuffer( CameraEvent.BeforeForwardOpaque, command_buffer );
        }
    }

    #region shadow map slot allocation
    /// <summary>
    /// Sets up a light, assigning / binding it to a slot (row) in the shadow map.
    /// </summary>
    /// <param name="light">The light to register / bind.</param>
    public void RegisterLight( ShadowCastingLight light )
    {
        int slot = AllocateShadowMap();
        if ( slot != -1 )
        {
            light.shadow_map_slot = slot;
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogError( "Too many shadow casting lights in the scene!" );
            #endif
        }
    }

    /// <summary>
    /// Removes a light's binding from the shadow map, disabling it from casting shadows.
    /// </summary>
    /// <param name="light">The light to unregister / unbind.</param>
    public void UnregisterLight( ShadowCastingLight light )
    {
        if ( light.shadow_map_slot < 0 || light.shadow_map_slot > MAX_SHADOW_MAPS ) { Debug.LogError( "Error freeing shadow map slot." ); }
        FreeShadowMap( light.shadow_map_slot );
        light.shadow_map_slot = -1;
    }

    /// <summary>
    /// Allocates the first available y coordinate of the shadowmap render texture to this light.
    /// </summary>
    /// <returns>The y coordinate in the shadowmap render texture that this light will use</returns>
    private int AllocateShadowMap()
    {
        for ( int i = 0; i < MAX_SHADOW_MAPS; i++ )
        {
            ulong mask = ( (ulong) 1 ) << i;
            if ( ( allocated_shadow_maps & mask ) == 0 )
            {
                allocated_shadow_maps |= mask; // bit 0->1
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Frees the specified y coordinate of the shadowmap render texture.
    /// </summary>
    /// <param name="slot">The y coordinate of the shadowmap render texture to free</param>
    private void FreeShadowMap( int slot )
    {
        if ( slot >= 0 )
        {
            ulong mask = ( (ulong) 1 ) << slot;
            Debug.Assert( ( allocated_shadow_maps & mask ) != 0 );
            allocated_shadow_maps &= ~ mask; // bit 1->0
        }
    }
    #endregion

    #region mesh generators
    private Mesh GetBlockerMesh()
    {
        // Get all light-occluding geometry in the level
        LightBlocker[] blockers = top_level_object.GetComponentsInChildren<LightBlocker>(); // inefficient
        // Get all edges of the geometry
        List<Vector2> edges = new List<Vector2>();
        foreach ( LightBlocker blocker in blockers )
        {
            blocker.GetEdges( ref edges );
        }

        // Jam the edge data + minimum data set into a mesh, to pass to ShadowMap.shader
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> vertices2 = new List<Vector2>();
        for ( int i = 0; i < edges.Count; i += 2 )
        {
            // pass edge vert1->vert2 to the shader:
            vertices.Add( edges[ i ] );
            vertices2.Add( edges[ i + 1 ] );
            // pass the same edge, reversed. (may be unneeded)
            vertices.Add( edges[ i + 1 ] );
            vertices2.Add( edges[ i ] );
        }

        // simplest index buffer
        int[] indecies = new int[ edges.Count ];
        for ( int i = 0; i < edges.Count; i++ )
        {
            indecies[ i ] = i;
        }

        Mesh mesh = new Mesh();
        mesh.SetVertices( vertices ); // pass vertex 1
        mesh.SetUVs( 0, vertices2 );  // pass vertex 2
        mesh.SetIndices( indecies, MeshTopology.Lines, 0 );
        return mesh;
    }

    // Make a simple mesh suitable for doing a fullscreen shader
    // pass, e.g. fills the screen with uvs going from (0,0) to (1,1)
    private Mesh FullscreenRenderMesh()
    {
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs0 = new List<Vector2>();
        int[] indices = new int[6];

        verts.Add( new Vector3( -1.0f, +1.0f, 0.0f ) );
        verts.Add( new Vector3( +1.0f, +1.0f, 0.0f ) );
        verts.Add( new Vector3( +1.0f, -1.0f, 0.0f ) );
        verts.Add( new Vector3( -1.0f, -1.0f, 0.0f ) );

        uvs0.Add( new Vector2( 0.0f, 0.0f ) );
        uvs0.Add( new Vector2( 1.0f, 0.0f ) );
        uvs0.Add( new Vector2( 1.0f, 1.0f ) );
        uvs0.Add( new Vector2( 0.0f, 1.0f ) );

        indices[ 0 ] = 0;
        indices[ 1 ] = 1;
        indices[ 2 ] = 2;
        indices[ 3 ] = 0;
        indices[ 4 ] = 2;
        indices[ 5 ] = 3;

        Mesh mesh = new Mesh();
        mesh.SetVertices( verts );
        mesh.SetUVs( 0, uvs0 );
        mesh.SetIndices( indices, MeshTopology.Triangles, 0 );
        return mesh;
    }

    // Makes a simple mesh for rendering shadow maps in world space
    private Mesh FullscreenRenderMeshWorld()
    {
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs0 = new List<Vector2>();
        int[] indices = new int[6];

        Vector3 depth_modifier = (Camera.main.transform.position.z + 1.0f) * new Vector3( 0.0f, 0.0f, 1.0f);
        verts.Add( Camera.main.ScreenToWorldPoint( new Vector3( 0.0f, Screen.height, 0.0f )         - depth_modifier ) );
        verts.Add( Camera.main.ScreenToWorldPoint( new Vector3( Screen.width, Screen.height, 0.0f ) - depth_modifier ) );
        verts.Add( Camera.main.ScreenToWorldPoint( new Vector3( Screen.width, 0.0f, 0.0f )          - depth_modifier ) );
        verts.Add( Camera.main.ScreenToWorldPoint( new Vector3( 0.0f, 0.0f, 0.0f )                  - depth_modifier ) );

        uvs0.Add( new Vector2( 0.0f, 0.0f ) );
        uvs0.Add( new Vector2( 1.0f, 0.0f ) );
        uvs0.Add( new Vector2( 1.0f, 1.0f ) );
        uvs0.Add( new Vector2( 0.0f, 1.0f ) );

        indices[ 0 ] = 0;
        indices[ 1 ] = 1;
        indices[ 2 ] = 2;
        indices[ 3 ] = 0;
        indices[ 4 ] = 2;
        indices[ 5 ] = 3;

        Mesh mesh = new Mesh();
        mesh.SetVertices( verts );
        mesh.SetUVs( 0, uvs0 );
        mesh.SetIndices( indices, MeshTopology.Triangles, 0 );
        return mesh;
    }
    #endregion

    /// <summary>
    /// Inverts the u coordinate on some graphics architectures.
    /// </summary>
    /// <param name="raw_u">The raw u coordinate.</param>
    /// <returns>The u coordinate, inverted if it needs to be.</returns>
    private float UCoordinate( float raw_u )
    {
        if (   ( SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore )
            || ( SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 )
            || ( SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 ) )
        {
            return raw_u;
        }
        return 1.0f - raw_u;
    }

    // Called before the camera renders
    private void OnPreRender()
    {
        // Get all lights and light occluders, to pass to the shadow mapper
        Mesh blocker_mesh = GetBlockerMesh();
        ShadowCastingLight[] lights = top_level_object.GetComponentsInChildren<ShadowCastingLight>(); // inefficient.

        ShadowMap( ref blocker_mesh, ref lights );
        ConsolidateShadowMap();
        RenderShadows( ref lights );
    }

    /// <summary>
    /// Creates a raw shadow map
    /// </summary>
    /// <param name="blocker_mesh">A mesh made of the edges of all light-occluding geometry.</param>
    /// <param name="lights">An array of all the lights in the scene</param>
    private void ShadowMap( ref Mesh blocker_mesh, ref ShadowCastingLight[] lights )
    {
        // Flush the raw shadow map
        command_buffer.Clear();
        command_buffer.SetRenderTarget( raw_shadow_map );
        command_buffer.ClearRenderTarget( true, true, new Color( 1, 1, 1, 1 ), 1.0f );

        // for each light
        foreach ( ShadowCastingLight light in lights )
        {
            if ( light.shadow_map_slot == -1 )
            {
                #if UNITY_EDITOR
                Debug.LogError( "Error: unslotted shadowmap light source. This light source will be ignored. (Are there > 64 lights in the scene?)" );
                #endif
                continue; // unallocated light, skip it.
            } 

            // set shader properties
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            properties.SetVector( "_LightPosition", new Vector4( light.transform.position.x, light.transform.position.y, 0.0f, 0.0f ) );
            properties.SetFloat( "_ShadowMapY", ( (float) light.shadow_map_slot / (float) MAX_SHADOW_MAPS * 2.0f ) - 1.0f ); // ( -1, 1 ) range
            // shadow map the light vs. occluders in the raw shadow map
            command_buffer.DrawMesh( blocker_mesh, Matrix4x4.identity, raw_shadow_map_material, 0, -1, properties );
        }
    }

    /// <summary>
    /// Consolidates the raw shadow map, removing the extra 180 degree wraparound zone.
    /// This pass reduces shadow map size 33%, increasing the performance of shadow rendering.
    /// </summary>
    private void ConsolidateShadowMap()
    {
        shadow_map_material.SetTexture( "_ShadowMap", raw_shadow_map );
        command_buffer.SetRenderTarget( shadow_map );
        command_buffer.DrawMesh( FullscreenRenderMesh(), Matrix4x4.identity, shadow_map_material );
    }

    /// <summary>
    /// Renders shadows based on the consolidated shadow map to a full-screen render texture, 
    /// then passes it to the shadow renderer to be queued up for normal rendering.
    /// </summary>
    /// <param name="lights">An array of all the lights in the scene</param>
    private void RenderShadows( ref ShadowCastingLight[] lights )
    {
        // Render all the shadows based on the shadowmap to the screen.
        Material shadow_material = new Material( shadow_shader );

        command_buffer.SetRenderTarget( shadow );
        command_buffer.ClearRenderTarget( true, true, new Color( 0, 0, 0, 0 ), 1.0f ); // paint it transparent black.

        // need to draw each light to the shadow texture independently, and blend it with previously drawn lights.
        foreach ( ShadowCastingLight light in lights )
        {
            MaterialPropertyBlock properties2 = new MaterialPropertyBlock();
            properties2.SetTexture( "_ShadowMap", shadow_map );
            properties2.SetTexture( "_BlendTarget", shadow );
            properties2.SetVector( "_LightPosition", new Vector4( light.transform.position.x, light.transform.position.y, 0.0f, 0.0f ) );
            properties2.SetColor( "_Color", new Color( 1.0f, 1.0f, 1.0f, 0.10f ) );
            // add a small tolerance to prevent ambiguous sampling from using the wrong shadowmap.
            properties2.SetFloat( "_ShadowMapY", UCoordinate( ( ( (float) light.shadow_map_slot ) + 0.5f ) / ( (float) MAX_SHADOW_MAPS ) ) ); // ( 0, 1 ) range
            // draw it, and blend.
            command_buffer.DrawMesh( FullscreenRenderMeshWorld(), Matrix4x4.identity, shadow_material, 0, -1, properties2 );
        }

        // set up shadow texture for rendering using the render queue and z depth testing
        shadow_object.GetComponent<MeshRenderer>().material.SetTexture( "_MainTex", shadow );
    }

    // Called after the camera renders, for post-processing
    private void OnRenderImage( RenderTexture source, RenderTexture destination )
    {
        // Do not blit the shadow map here, by this point, it should be done with proper layering via the scene's ShadowRenderer.

        // Desaturate
        RenderTexture render_texture = new RenderTexture( source );
        if ( desaturation == 0.0f )
        {
            Graphics.Blit( source, render_texture );
        }
        else
        {
            desaturate_material.SetFloat( "_Desaturate", desaturation );
            Graphics.Blit( source, render_texture, desaturate_material );
        }

        // Night Vision Goggles
        RenderTexture render_texture2 = new RenderTexture( render_texture );
        if ( ! is_night_vision_on )
        {
            Graphics.Blit( render_texture, render_texture2 );
        }
        else
        {
            Graphics.Blit( render_texture, render_texture2, NVG_material );
        }
        render_texture.Release();

        Graphics.Blit( render_texture2, destination );
        render_texture2.Release();
    }

    // Called when this script is destroyed.
    private void OnDestroy()
    {
        shadow.Release();
    }
}