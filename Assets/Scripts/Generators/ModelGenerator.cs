﻿using B83.Image.BMP;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenMafia
{
    public class ModelGenerator : BaseGenerator
    {
        public override GameObject LoadObject(string path)
        {
            GameObject rootObject = LoadCachedObject(path);

            if (rootObject == null)
                rootObject = new GameObject(path);
            else
                return rootObject;
            
            FileStream fs;

            try
            {
                fs = new FileStream(GameManager.instance.fileSystem.GetCanonicalPath(path), FileMode.Open);
            }
            catch (Exception ex)
            {
                GameObject.DestroyImmediate(rootObject);
                Debug.LogWarning(ex.ToString());
                return null;
            }

            using (BinaryReader reader = new BinaryReader(fs))
            {
                var modelLoader = new MafiaFormats.Reader4DS();
                var model = modelLoader.loadModel(reader);

                var meshId = 0;

                var children = new List<KeyValuePair<int, Transform>>();

                foreach (var mafiaMesh in model.meshes)
                {
                    var child = new GameObject(mafiaMesh.meshName, typeof(MeshRenderer), typeof(MeshFilter));
                    var meshFilter = child.GetComponent<MeshFilter>();
                    var meshRenderer = child.GetComponent<MeshRenderer>();

                    children.Add(new KeyValuePair<int, Transform>(mafiaMesh.parentID, child.transform));

                    if (mafiaMesh.meshType != MafiaFormats.MeshType.MESHTYPE_STANDARD)
                        continue;

                    if (mafiaMesh.standard.instanced != 0)
                        continue;
                    
                    Material[] materials;

                    switch (mafiaMesh.visualMeshType)
                    {
                        case MafiaFormats.VisualMeshType.VISUALMESHTYPE_STANDARD:
                        {
                            // TODO build up more lods
                            if (mafiaMesh.standard.lods.Count > 0)
                            {
                                meshFilter.mesh = GenerateMesh(mafiaMesh, child, mafiaMesh.standard.lods[0], model, out materials);
                                meshRenderer.materials = materials;
                            }
                            else
                                continue;
                        }
                        break;

                        case MafiaFormats.VisualMeshType.VISUALMESHTYPE_SINGLEMORPH:
                        {
                            meshFilter.mesh = GenerateMesh(mafiaMesh, child, mafiaMesh.singleMorph.singleMesh.standard.lods[0], model, out materials);
                            meshRenderer.materials = materials;
                        }
                        break;

                        // TODO add more visual types

                        default: continue;
                    }
                    
                    meshId++;
                }

                for (int i = 0; i < children.Count; i++)
                {
                    var parentId = children[i].Key;
                    var mafiaMesh = model.meshes[i];

                    if (parentId > 0)
                        children[i].Value.parent = children[parentId - 1].Value;
                    else
                        children[i].Value.parent = rootObject.transform;

                    children[i].Value.localPosition = mafiaMesh.pos;
                    children[i].Value.localRotation = mafiaMesh.rot;
                    children[i].Value.localScale = mafiaMesh.scale;
                }

                children.Clear();
            }
            
            StoreChachedObject(path, rootObject);
            

            fs.Close();

            return rootObject;
        }

        Mesh GenerateMesh(MafiaFormats.Mesh mafiaMesh, GameObject ent, MafiaFormats.LOD firstMafiaLOD, MafiaFormats.Model model, out Material[] materials)
        {
            var mesh = new Mesh();
            
            var bmp = new BMPLoader();
            List<Material> mats = new List<Material>();

            List<Vector3> unityVerts = new List<Vector3>();
            List<Vector3> unityNormals = new List<Vector3>();
            List<Vector2> unityUV = new List<Vector2>();

            foreach (var vert in firstMafiaLOD.vertices)
            {
                unityVerts.Add(vert.pos);
                unityNormals.Add(vert.normal);
                unityUV.Add(new Vector2(vert.uv.x, -1 * vert.uv.y));
            }
            
            mesh.name = mafiaMesh.meshName;

            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUV);
            mesh.SetNormals(unityNormals);

            mesh.subMeshCount = firstMafiaLOD.faceGroups.Count;

            var faceGroupId = 0;

            foreach (var faceGroup in firstMafiaLOD.faceGroups)
            {
                List<int> unityIndices = new List<int>();
                foreach (var face in faceGroup.faces)
                {
                    unityIndices.Add(face.a);
                    unityIndices.Add(face.b);
                    unityIndices.Add(face.c);
                }

                mesh.SetTriangles(unityIndices.ToArray(), faceGroupId);

                var matId = (int)Mathf.Max(0, Mathf.Min(model.materials.Count - 1, faceGroup.materialID - 1));

                if (model.materials.Count > 0)
                {
                    var mafiaMat = model.materials[matId];

                    Material mat;

                    if ((mafiaMat.flags & MafiaFormats.MaterialFlag.MATERIALFLAG_COLORKEY) != 0)
                    {
                        mat = new Material(Shader.Find("Standard"));
                        mat.SetFloat("_Mode", 1f); // Set rendering mode to Cutout
                        mat.SetFloat("_Glossiness", 0f);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 1);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 3000;
                    }
                    else if (mafiaMat.transparency < 1)
                    {
                        mat = new Material(Shader.Find("Standard"));
                        mat.SetFloat("_Mode", 3f); // Set rendering mode to Transparent
                        mat.SetFloat("_Glossiness", 0f);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 1);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 3000;
                    }
                    else
                    {
                        mat = new Material(Shader.Find("Standard"));
                        mat.SetFloat("_Glossiness", 0f);
                    }

                    //if (matId > 0)
                    {

                        // TODO support more types as well as transparency

                        if (mafiaMat.diffuseMapName != null ||
                            mafiaMat.alphaMapName != null)
                        {
                            if ((mafiaMat.flags & MafiaFormats.MaterialFlag.MATERIALFLAG_COLORKEY) != 0)
                                BMPLoader.useTransparencyKey = true;

                            BMPImage image = null;

                            if ((mafiaMat.flags & MafiaFormats.MaterialFlag.MATERIALFLAG_TEXTUREDIFFUSE) != 0)
                                image = bmp.LoadBMP(GameManager.instance.fileSystem.GetCanonicalPath("maps/" + mafiaMat.diffuseMapName));
                            else if (mafiaMat.alphaMapName != null)
                                image = bmp.LoadBMP(GameManager.instance.fileSystem.GetCanonicalPath("maps/" + mafiaMat.alphaMapName));

                            BMPLoader.useTransparencyKey = false;

                            if (image != null)
                            {
                                Texture2D tex = image.ToTexture2D();

                                if ((mafiaMat.flags & MafiaFormats.MaterialFlag.MATERIALFLAG_TEXTUREDIFFUSE) != 0)
                                    tex.name = mafiaMat.diffuseMapName;
                                else if (mafiaMat.alphaMapName != null)
                                    tex.name = mafiaMat.alphaMapName;

                                mat.SetTexture("_MainTex", tex);

                                if (GameManager.instance.cvarManager.Get("filterMode", "1") == "0")
                                    tex.filterMode = FilterMode.Point;
                            }

                            if (mafiaMat.transparency < 1)
                                mat.SetColor("_Color", new Color32(255, 255, 255, (byte)(mafiaMat.transparency * 255)));

                            if ((mafiaMat.flags & (MafiaFormats.MaterialFlag.MATERIALFLAG_ANIMATEDTEXTUREDIFFUSE | MafiaFormats.MaterialFlag.MATERIALFLAG_ANIMATEXTEXTUREALPHA)) != 0)
                            {
                                List<Texture2D> frames = new List<Texture2D>();

                                string fileName = null;

                                if ((mafiaMat.flags & MafiaFormats.MaterialFlag.MATERIALFLAG_ANIMATEDTEXTUREDIFFUSE) != 0)
                                    fileName = mafiaMat.diffuseMapName;
                                else
                                    fileName = mafiaMat.alphaMapName;

                                if ((mafiaMat.flags & MafiaFormats.MaterialFlag.MATERIALFLAG_COLORKEY) != 0)
                                    BMPLoader.useTransparencyKey = true;

                                if (fileName != null)
                                {
                                    var path = fileName.Split('.');
                                    string baseName = path[0];
                                    string ext = path[1];

                                    baseName = baseName.Substring(0, baseName.Length - 2);

                                    for (int k = 0; k < mafiaMat.animSequenceLength; k++)
                                    {
                                        var frameImage = bmp.LoadBMP(GameManager.instance.fileSystem.GetCanonicalPath("maps/" + baseName + "0" + k + "." + ext));

                                        if (frameImage == null)
                                            continue;

                                        var frame = frameImage.ToTexture2D();
                                        frames.Add(frame);
                                    }

                                    var framePlayer = ent.AddComponent<TextureAnimationPlayer>();

                                    framePlayer.frames = frames;
                                    framePlayer.framePeriod = mafiaMat.framePeriod;
                                    framePlayer.material = mat;
                                }

                                BMPLoader.useTransparencyKey = false;
                            }
                        }
                    }

                    mats.Add(mat);
                }

                faceGroupId++;
            }

            materials = mats.ToArray();

            return mesh;
        }
    }
}