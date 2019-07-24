using System.IO;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System;

public class ExportSplatmap : EditorWindow
{
    int splitTime;
    int alphaMapCount = 4;
    string path;
    List<TextureSort> textureSortData = new List<TextureSort>();

    private UnityEngine.Object terrainDataAsset;

    [MenuItem("TerrainTools/ExportSplatmap")]
    static void Init()
    {
        EditorWindow window = GetWindow(typeof(ExportSplatmap));
        window.Show();
    }
    void OnGUI()
    {
        splitTime = EditorGUILayout.IntField("SplitTime:", splitTime);
        if (GUILayout.Button("Split & Export"))
        {
            Terrain terrain = Selection.activeObject as Terrain;
            if (!terrain)
            {
                terrain = Terrain.activeTerrain;
                if (!terrain)
                {
                    Debug.Log("Could not find any terrain. Please select or create a terrain first.");
                    return;
                }
            }
            TerrainData terrainData = terrain.terrainData;
            int alphaMapsCount = terrainData.alphamapTextureCount;

            if (alphaMapsCount < 1)
            {
                Debug.LogError("Split time can't be less than 1");
                return;
            }
            path = EditorUtility.SaveFolderPanel("Choose a directory to save the alpha maps:", "", "");
            if (path != null && path.Length != 0)
            {
                path = path.Replace(Application.dataPath, "Assets");
            }
            else
            {
                return;
            }

            StreamWriter sw = File.CreateText(path + "/" + "Configuration.csv");

            Texture2D[] alphamapTextures = terrainData.alphamapTextures;
            Texture2D heightmapTexture = ConvertToTexture2D(terrainData.heightmapTexture);
            int unitWidth = (terrainData.alphamapWidth - 1) / (splitTime + 1);
            Color[] colorCache = null;
            Texture2D alphaTexCache = new Texture2D(unitWidth, unitWidth);
            Texture2D heightMapCache = new Texture2D(unitWidth + 1, unitWidth + 1);

            TerrainData subTerrainData = null;
            for (int y = 0; y <= splitTime; y++)
            {
                for (int x = 0; x <= splitTime; x++)
                {
                    textureSortData.Clear();
                    for (int i = 0; i < alphaMapsCount; i++)
                    {
                        colorCache = alphamapTextures[i].GetPixels(0 + unitWidth * x, 0 + unitWidth * y, unitWidth, unitWidth);
                        //Debug.Log(alphaMapsCount);
                        float[] colorWeight = new float[4];
                        for (int m = 0; m < colorCache.Length; m++)
                        {
                            colorWeight[0] += colorCache[m].r;
                            colorWeight[1] += colorCache[m].g;
                            colorWeight[2] += colorCache[m].b;
                            colorWeight[3] += colorCache[m].a;
                        }
                        //设置高度图数据
                        colorCache = heightmapTexture.GetPixels(0 + unitWidth * x, 0 + unitWidth * y, unitWidth + 1, unitWidth + 1);
                        textureSortData.Add(new TextureSort(i * 4, colorWeight[0]));
                        textureSortData.Add(new TextureSort(i * 4 + 1, colorWeight[1]));
                        textureSortData.Add(new TextureSort(i * 4 + 2, colorWeight[2]));
                        textureSortData.Add(new TextureSort(i * 4 + 3, colorWeight[3]));
                    }
                    //根据SplatMap颜色权重排序
                    textureSortData.Sort();

                    //----------------------------------------------------------------//
                    //-------------------------需要修订-------------------------------//
                    //----------------------------------------------------------------//
                    float[,,] subAlphaTex = new float[unitWidth + 1, unitWidth + 1, alphaMapCount];
                    float[,,] alphaTex = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
                    Color[] colors = new Color[unitWidth * unitWidth];
                    //配置输出贴图数，当前为4
                    for (int i = 0; i < alphaMapCount; i++)
                    {
                        int layerIndex = textureSortData[i].terrainLayerIndex;
                        Color[] temp = alphamapTextures[layerIndex / 4].GetPixels(0 + unitWidth * x, 0 + unitWidth * y, unitWidth, unitWidth);
                        for (int m = 0; m < colors.Length; m++)
                        {
                            colors[m][i] = temp[m][layerIndex % 4];

                            //注：float[x,y,z]分别代表在x,y处的第z个splatMap权值
                        }
                        for (int y1 = 0; y1 < unitWidth; ++y1)
                        {
                            for (int x1 = 0; x1 < unitWidth; ++x1)
                            {
                                //注：float[y,x,z]分别代表在(x,y)处的第z个splatMap权值
                                subAlphaTex[y1, x1, i] = alphaTex[unitWidth * y + y1, unitWidth * x + x1, layerIndex];
                            }   
                        }
                    }
                    //normalize weights in new alpha map
                    for (int y1 = 0; y1 < unitWidth; ++y1)
                    {
                        for (int x1 = 0; x1 < unitWidth; ++x1)
                        {
                            float sum = 0.0F;
                            for (int a = 0; a < alphaMapCount; ++a)
                                sum += subAlphaTex[y1, x1, a];
                            if (sum >= 0.01)
                            {
                                float multiplier = 1.0F / sum;
                                for (int a = 0; a < alphaMapCount; ++a)
                                    subAlphaTex[y1, x1, a] *= multiplier;
                            }
                            else
                            {
                                // in case all weights sum to pretty much zero (e.g.
                                // removing splat that had 100% weight), assign
                                // everything to 1st splat texture (just like
                                // initial terrain).
                                for (int a = 0; a < alphaMapCount; ++a)
                                    subAlphaTex[y1, x1, a] = (a == 0) ? 1.0f : 0.0f;
                            }
                        }
                    }

                    alphaTexCache.name = "Texture";
                    heightMapCache.name = "HeightMap";
                    alphaTexCache.SetPixels(colors);
                    ConvertToGray(colorCache);
                    heightMapCache.SetPixels(colorCache);
                    alphaTexCache.Apply();
                    heightMapCache.Apply();
                    byte[] pngDataTmp = alphaTexCache.EncodeToPNG();
                    File.WriteAllBytes(path + "/" + alphaTexCache.name + "_" + y + "_" + x + ".png", pngDataTmp);
                    pngDataTmp = heightMapCache.EncodeToPNG();
                    File.WriteAllBytes(path + "/" + heightMapCache.name + "_" + y + "_" + x + ".png", pngDataTmp);
                    sw.WriteLine(string.Format("{0},{1},{2},{3},{4}",
                        alphaTexCache.name + "_" + y + "_" + x,
                        terrainData.terrainLayers[textureSortData[0].terrainLayerIndex],
                        terrainData.terrainLayers[textureSortData[1].terrainLayerIndex],
                        terrainData.terrainLayers[textureSortData[2].terrainLayerIndex],
                        terrainData.terrainLayers[textureSortData[3].terrainLayerIndex]
                    ));

                    //Create Sub-Terrain
                    //此处有严格顺序：必须先设置Resolution再设置size，否则size会莫名其妙重置
                    subTerrainData = new TerrainData();
                    subTerrainData.heightmapResolution = unitWidth + 1;
                    subTerrainData.size = new Vector3(terrainData.size.x / (splitTime + 1), terrainData.size.y, terrainData.size.z / (splitTime + 1));
                    subTerrainData.alphamapResolution = unitWidth + 1;
                    RenderTexture.active = terrainData.heightmapTexture;
                    subTerrainData.CopyActiveRenderTextureToHeightmap(new RectInt(0 + unitWidth * x, 0 + unitWidth * y, unitWidth + 1, unitWidth + 1), Vector2Int.zero, TerrainHeightmapSyncControl.HeightAndLod);
                    RenderTexture.active = null;

                    //subTerrainData.SetAlphamaps(terrainData.GetAlphamaps(0 + unitWidth * k, 0 + unitWidth * j, unitWidth + 1, unitWidth + 1));
                    //for (var idx = 0; idx < subTerrainData.terrainLayers.Length; ++idx)
                    //{
                    //    if (subTerrainData.terrainLayers[idx] == tmpLayer)
                    //        return;
                    //}
                    //int newIndex = subTerrainData.terrainLayers.Length;
                    //var newarray = new TerrainLayer[newIndex + 1];
                    //Array.Copy(subTerrainData.terrainLayers, 0, newarray, 0, newIndex);
                    //newarray[newIndex] = tmpLayer;

                    AssetDatabase.CreateAsset(subTerrainData, path + "/" + terrainData.name + "_" + y + "_" + x + ".asset");

                    TerrainLayer[] terrainLayers = new TerrainLayer[4];
                    for (int i = 0; i < 4; i++)
                    {
                        //拷贝Layer，完整版应该拷贝所有数据，目前仅实现部分作验证；
                        TerrainLayer tmpLayer = new TerrainLayer();
                        tmpLayer.name = terrainData.terrainLayers[textureSortData[0].terrainLayerIndex].name;
                        tmpLayer.diffuseTexture = terrainData.terrainLayers[textureSortData[i].terrainLayerIndex].diffuseTexture;
                        tmpLayer.tileSize = terrainData.terrainLayers[textureSortData[i].terrainLayerIndex].tileSize;
                        tmpLayer.tileOffset = terrainData.terrainLayers[textureSortData[i].terrainLayerIndex].tileOffset;
                        terrainLayers[i] = tmpLayer;
                        AssetDatabase.AddObjectToAsset(tmpLayer, subTerrainData);
                    }
                    //Terrain.CreateTerrainGameObject(subTerrainData).name = terrainData.name + "_" + j + "_" + k;
                    subTerrainData.terrainLayers = terrainLayers;
                    subTerrainData.SetAlphamaps(0, 0, subAlphaTex);
                    Debug.Log(terrainData.GetAlphamaps(0, 0, unitWidth + 1, unitWidth + 1).GetLength(2));
                    Debug.Log(subTerrainData.GetAlphamaps(0, 0, unitWidth + 1, unitWidth + 1).GetLength(2));
                    AssetDatabase.SaveAssets();
                }
            }
            sw.Close();
        }
    }
    Texture2D ConvertToTexture2D(RenderTexture rTex)
    {
        Texture2D tex = new Texture2D(rTex.width, rTex.width, TextureFormat.RGB24, false);
        RenderTexture.active = rTex;
        tex.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        return tex;
    }

    //RenderTexture ConvertToRenderTexture(Texture2D tex2D)
    //{
    //    RenderTexture rTex = new RenderTexture(tex2D.width, tex2D.width, 8);
    //}

    void ConvertToGray(in Color[] colors)
    {
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = new Color(colors[i].r, colors[i].r, colors[i].r, 1f);
        }
    }
}

public class TextureSort : IComparable<TextureSort>
{
    public int terrainLayerIndex;
    public float splatWeights;

    public TextureSort(int terrainLayerIndex, float splatWeights)
    {
        this.terrainLayerIndex = terrainLayerIndex;
        this.splatWeights = splatWeights;
    }

    int IComparable<TextureSort>.CompareTo(TextureSort other)
    {
        if (splatWeights > other.splatWeights)
            return -1;
        else
            return 1;
    }
}