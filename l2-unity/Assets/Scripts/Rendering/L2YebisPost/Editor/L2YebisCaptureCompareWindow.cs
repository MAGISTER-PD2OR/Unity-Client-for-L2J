using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Loads one D3D9 combine capture and runs Hidden/L2/YebisCaptureReplay on that input.
/// The scene is not rendered. The client output is the reference.
/// </summary>
public sealed class L2YebisCaptureCompareWindow : EditorWindow
{
    const string GameCaptures =
        @"C:\games\Lineage II HighElfes\ADEU-P464-D20240703-P-240117-240724-1\system\logs\YebisCapture";

    const string PathPref = "L2.YebisCapture.Path";

    string _folder = "";
    string _report = "Папка draw_XXX из logs\\YebisCapture. Формула: s0, матрица, filmic, блик s1, гамма.";
    Vector2 _scroll;

    Texture2D _inputPreview;
    Texture2D _clientPreview;
    Texture2D _unityPreview;
    Texture2D _errorPreview;
    string _errorLabel = "Пиксели с разницей от 1 уровня";

    [MenuItem("L2/Yebis/Compare Capture")]
    static void Open()
    {
        var window = GetWindow<L2YebisCaptureCompareWindow>("Yebis Capture");
        window.minSize = new Vector2(760, 640);
        window._folder = EditorPrefs.GetString(PathPref, "");
    }

    void OnDisable()
    {
        DestroyPreview(ref _inputPreview);
        DestroyPreview(ref _clientPreview);
        DestroyPreview(ref _unityPreview);
        DestroyPreview(ref _errorPreview);
    }

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField("Снимок одного draw. Сцена Unity не рисуется.", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("s0 идёт в матрицу и filmic. s1 — блик. Если s1.raw нет, блик чёрный.");

        EditorGUILayout.BeginHorizontal();
        _folder = EditorGUILayout.TextField("Папка", _folder);
        if (GUILayout.Button("Выбрать", GUILayout.Width(90)))
        {
            string picked = EditorUtility.OpenFolderPanel("Папка draw_XXX", _folder, "");
            if (!string.IsNullOrEmpty(picked))
            {
                _folder = picked;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Последний снимок клиента"))
        {
            if (TryLatestDraw(out string latest))
            {
                _folder = latest;
            }
            else
            {
                _report = "В " + GameCaptures + " ещё нет draw с output.raw.";
            }
        }
        if (GUILayout.Button("Сравнить"))
        {
            Compare();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Снять живой вход"))
        {
            if (!EditorApplication.isPlaying)
            {
                _report = "Сначала Play Mode. Затем эта кнопка или клавиша G в окне Game. Солнечное время в Play держится на 12:00.";
            }
            else
            {
                L2YebisLiveCapture.Request();
                _report = "Кадр снимется на следующем проходе. Папка появится в YebisCapture\\unity_live.";
            }
        }
        if (GUILayout.Button("Последний живой снимок"))
        {
            if (TryLatestUnity(out string live))
            {
                _folder = live;
            }
            else
            {
                _report = "Живого снимка ещё нет. Play Mode, затем G или «Снять живой вход».";
            }
        }
        if (GUILayout.Button("Свести входы"))
        {
            CompareInputs();
        }
        if (GUILayout.Button("Сверить кривую"))
        {
            CompareCurve();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(_report, MessageType.None);
        DrawPair("Вход", _inputPreview, "Клиент после draw", _clientPreview);
        DrawPair("Unity", _unityPreview, _errorLabel, _errorPreview);
        EditorGUILayout.EndScrollView();
    }

    static void DrawPair(string leftLabel, Texture left, string rightLabel, Texture right)
    {
        EditorGUILayout.BeginHorizontal();
        DrawPreview(leftLabel, left);
        DrawPreview(rightLabel, right);
        EditorGUILayout.EndHorizontal();
    }

    static void DrawPreview(string label, Texture texture)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(360));
        EditorGUILayout.LabelField(label);
        Rect rect = GUILayoutUtility.GetRect(340, 190, GUILayout.ExpandWidth(true));
        if (texture != null)
        {
            EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
        }
        EditorGUILayout.EndVertical();
    }

    void Compare()
    {
        string folder = ResolveDrawFolder(_folder);
        if (string.IsNullOrEmpty(folder))
        {
            _report = "Нет output.raw. Выбери папку draw_XXX или корень снимка.";
            return;
        }

        _folder = folder;
        EditorPrefs.SetString(PathPref, folder);

        try
        {
            EditorUtility.DisplayProgressBar("Yebis capture", "Чтение входа", 0.2f);
            Dictionary<string, string> inputMeta = ReadMeta(Path.Combine(folder, "input.meta.txt"));
            Dictionary<string, string> outputMeta = ReadMeta(Path.Combine(folder, "output.meta.txt"));
            Dictionary<string, string> constants = ReadConstants(Path.Combine(folder, "constants.txt"));
            Dictionary<string, string> gpu = ReadMeta(Path.Combine(folder, "gpu-state.txt"));

            float[] input = LoadRaw(Path.Combine(folder, "input.raw"), inputMeta, out int width, out int height);
            EditorUtility.DisplayProgressBar("Yebis capture", "Чтение выхода клиента", 0.4f);
            float[] client = LoadRaw(Path.Combine(folder, "output.raw"), outputMeta, out int outWidth, out int outHeight);

            bool hasGlare = TryLoadOptional(folder, "s1", out float[] glare, out int glareWidth, out int glareHeight);
            bool hasDither = TryLoadOptional(folder, "s3", out float[] dither, out int ditherWidth, out int ditherHeight);
            int compareWidth = Math.Min(width, outWidth);
            int compareHeight = Math.Min(height, outHeight);
            bool quantize8 = Value(outputMeta, "memoryOrder") == "B8G8R8A8" || Value(outputMeta, "memoryOrder") == "R8G8B8A8";

            EditorUtility.DisplayProgressBar("Yebis capture", "Прогон формулы", 0.7f);
            float[] unity = RunGrade(input, width, height, glare, glareWidth, glareHeight, hasGlare, dither, ditherWidth, ditherHeight, hasDither, constants, gpu);
            EditorUtility.DisplayProgressBar("Yebis capture", "Разница", 0.9f);

            var stats = new StringBuilder();
            stats.AppendLine(hasDither ? "С дизером, уровни байта 0..255:" : "Уровни байта 0..255:");
            float[] error = BuildError(unity, width, client, outWidth, compareWidth, compareHeight, stats);
            if (hasDither)
            {
                float[] unityOff = RunGrade(input, width, height, glare, glareWidth, glareHeight, hasGlare, dither, ditherWidth, ditherHeight, false, constants, gpu);
                var off = new StringBuilder();
                BuildError(unityOff, width, client, outWidth, compareWidth, compareHeight, off);
                stats.AppendLine("Без дизера, уровни байта 0..255:");
                stats.Append(off);
            }
            AppendUvNote(stats, folder);
            AppendSourceBands(stats, "Вход s0", input, width, height);
            float[] shown = error;
            _errorLabel = "Пиксели с разницей от 1 уровня";
            if (hasGlare)
            {
                float[] blackGlare = RunGrade(input, width, height, glare, glareWidth, glareHeight, false, dither, ditherWidth, ditherHeight, hasDither, constants, gpu);
                shown = BuildPairDelta(unity, blackGlare, width, height, stats);
                _errorLabel = "Вклад s1: серое 1 уровень, белое 2+";
            }

            DestroyPreview(ref _inputPreview);
            DestroyPreview(ref _clientPreview);
            DestroyPreview(ref _unityPreview);
            DestroyPreview(ref _errorPreview);
            _inputPreview = MakeTexture(input, width, height);
            _clientPreview = MakeTexture(client, outWidth, outHeight);
            _unityPreview = MakeTexture(unity, width, height);
            int shownWidth = hasGlare ? width : compareWidth;
            int shownHeight = hasGlare ? height : compareHeight;
            _errorPreview = MakeTexture(shown, shownWidth, shownHeight);

            var header = new StringBuilder();
            header.AppendLine(folder);
            header.AppendLine("Вход " + width + "x" + height + " " + Value(inputMeta, "formatName") + " " + Value(inputMeta, "memoryOrder"));
            header.AppendLine("Выход " + outWidth + "x" + outHeight + " " + Value(outputMeta, "formatName") + " " + Value(outputMeta, "memoryOrder"));
            if (width != outWidth || height != outHeight)
            {
                header.AppendLine("Размеры различаются. Сравнение идёт по общей области " + compareWidth + "x" + compareHeight + ".");
            }
            AppendConstant(header, constants, "c4");
            AppendConstant(header, constants, "c12");
            AppendConstant(header, constants, "c14");
            header.AppendLine(hasGlare
                ? "s1 загружен, " + glareWidth + "x" + glareHeight + "."
                : "s1.raw нет. Блик чёрный, в формуле остаётся смещение 0.005 второй матрицы.");
            header.AppendLine(hasDither
                ? "s3 загружен, " + ditherWidth + "x" + ditherHeight + ". Дизер включён."
                : "s3.raw нет. Дизер выключен, сравнивается ветка до него.");
            header.AppendLine("Pre3898, экспозиция и SatMix в этот прогон не входят.");
            if (quantize8)
            {
                header.AppendLine("Выход клиента 8-битный. Разница считается в целых уровнях 0..255.");
            }
            if (Value(gpu, "srgbWrite") == "1" || Value(gpu, "sampler0Srgb") == "1")
            {
                header.AppendLine("В снимке включён sRGB write или sampler. Сырые числа загружены без дополнительной гаммы.");
            }
            header.Append(stats);
            _report = header.ToString();
            File.WriteAllText(Path.Combine(folder, "unity_compare.txt"), _report, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _report = ex.Message;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    float[] RunGrade(
        float[] input,
        int width,
        int height,
        float[] glare,
        int glareWidth,
        int glareHeight,
        bool hasGlare,
        float[] dither,
        int ditherWidth,
        int ditherHeight,
        bool hasDither,
        Dictionary<string, string> constants,
        Dictionary<string, string> gpu)
    {
        Shader shader = Shader.Find("Hidden/L2/YebisCaptureReplay");
        if (shader == null)
        {
            throw new InvalidOperationException("Не найден шейдер Hidden/L2/YebisCaptureReplay.");
        }

        Texture2D source = MakeTexture(input, width, height);
        Texture2D glareTex = hasGlare ? MakeTexture(glare, glareWidth, glareHeight) : BlackTex();
        Texture2D ditherTex = hasDither ? MakeTexture(dither, ditherWidth, ditherHeight) : BlackTex();
        source.filterMode = SamplerFilter(gpu, "sampler0Mag");
        glareTex.filterMode = SamplerFilter(gpu, "sampler1Mag");
        ditherTex.filterMode = SamplerFilter(gpu, "sampler3Mag");
        source.wrapMode = SamplerWrap(gpu, "sampler0AddressU");
        glareTex.wrapMode = SamplerWrap(gpu, "sampler1AddressU");
        ditherTex.wrapMode = SamplerWrap(gpu, "sampler3AddressU");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        BindGrade(material, constants, hasDither);
        material.SetTexture("_GlareTex", glareTex);
        material.SetTexture("_DitherTex", ditherTex);

        var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R32G32B32A32_SFloat, 0)
        {
            sRGB = false,
            mipCount = 1,
            msaaSamples = 1,
            useMipMap = false
        };
        var rt = new RenderTexture(desc)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        rt.Create();

        try
        {
            Graphics.Blit(source, rt, material);
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(rt, 0, GraphicsFormat.R32G32B32A32_SFloat);
            request.WaitForCompletion();
            if (request.hasError)
            {
                throw new InvalidOperationException("GPU не отдал результат прогона.");
            }

            NativeArray<float> raw = request.GetData<float>();
            var pixels = new float[raw.Length];
            raw.CopyTo(pixels);
            return pixels;
        }
        finally
        {
            DestroyImmediate(source);
            DestroyImmediate(glareTex);
            DestroyImmediate(ditherTex);
            DestroyImmediate(material);
            rt.Release();
            DestroyImmediate(rt);
        }
    }

    void BindGrade(Material material, Dictionary<string, string> constants, bool hasDither)
    {
        material.SetVector("_CapC4", VectorOf(constants, "c4", new Vector4(4.7551184f, 0.17701194f, 0.0178695f, 0.005f)));
        material.SetVector("_CapC5", VectorOf(constants, "c5", new Vector4(0.052618481f, 4.8795118f, 0.0178695f, 0.005f)));
        material.SetVector("_CapC6", VectorOf(constants, "c6", new Vector4(0.052618481f, 0.17701194f, 4.7203693f, 0.005f)));
        material.SetVector("_CapC7", VectorOf(constants, "c7", new Vector4(0.9510237f, 0.035402387f, 0.0035739001f, 0.005f)));
        material.SetVector("_CapC8", VectorOf(constants, "c8", new Vector4(0.010523696f, 0.97590238f, 0.0035739001f, 0.005f)));
        material.SetVector("_CapC9", VectorOf(constants, "c9", new Vector4(0.010523696f, 0.035402387f, 0.94407392f, 0.005f)));
        Vector4 filmic = VectorOf(constants, "c14", new Vector4(2.6581414f, 0.66499311f, 0f, 0f));
        material.SetFloat("_FilmicA", filmic.x);
        material.SetFloat("_FilmicB", filmic.y);
        Vector4 gamma = VectorOf(constants, "c12", new Vector4(0.625f, 0f, 0f, 0f));
        material.SetFloat("_GammaPow", gamma.x);
        material.SetVector("_Dither", VectorOf(constants, "c13", new Vector4(1f / 255f, 1f / 255f, -1f / 255f, 0f)));
        material.SetFloat("_HasDither", hasDither ? 1f : 0f);
    }

    static Texture2D BlackTex()
    {
        var tex = MakeTexture(new float[] { 0f, 0f, 0f, 1f }, 1, 1);
        return tex;
    }

    static FilterMode SamplerFilter(Dictionary<string, string> gpu, string key)
    {
        return Value(gpu, key) == "2" ? FilterMode.Bilinear : FilterMode.Point;
    }

    static TextureWrapMode SamplerWrap(Dictionary<string, string> gpu, string key)
    {
        string mode = Value(gpu, key);
        if (mode == "1")
        {
            return TextureWrapMode.Repeat;
        }
        if (mode == "2")
        {
            return TextureWrapMode.Mirror;
        }
        return TextureWrapMode.Clamp;
    }

    static bool TryLoadOptional(string folder, string name, out float[] pixels, out int width, out int height)
    {
        pixels = null;
        width = 0;
        height = 0;
        string raw = Path.Combine(folder, name + ".raw");
        string metaPath = Path.Combine(folder, name + ".meta.txt");
        if (!File.Exists(raw) || !File.Exists(metaPath))
        {
            return false;
        }
        Dictionary<string, string> meta = ReadMeta(metaPath);
        if (Value(meta, "present") == "0")
        {
            return false;
        }
        pixels = LoadRaw(raw, meta, out width, out height);
        return true;
    }

    static float[] BuildError(
        float[] unity,
        int unityWidth,
        float[] client,
        int clientWidth,
        int width,
        int height,
        StringBuilder stats)
    {
        var error = new float[width * height * 4];
        int count = width * height;
        int maxLevel = 0;
        int pixels0 = 0, pixels1 = 0, pixels2 = 0, pixelsMore = 0;
        int channels0 = 0, channels1 = 0, channels2 = 0, channelsMore = 0;
        int marked = 0, step1 = 0, step4x = 0, step4y = 0;
        var mark = new bool[count];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int u = (y * unityWidth + x) * 4;
                int c = (y * clientWidth + x) * 4;
                int e = (y * width + x) * 4;
                int dr = ChannelDelta(unity[u], client[c]);
                int dg = ChannelDelta(unity[u + 1], client[c + 1]);
                int db = ChannelDelta(unity[u + 2], client[c + 2]);
                CountChannel(dr, ref channels0, ref channels1, ref channels2, ref channelsMore);
                CountChannel(dg, ref channels0, ref channels1, ref channels2, ref channelsMore);
                CountChannel(db, ref channels0, ref channels1, ref channels2, ref channelsMore);
                int worst = Math.Max(dr, Math.Max(dg, db));
                maxLevel = Math.Max(maxLevel, worst);
                if (worst == 0) pixels0++;
                else if (worst == 1) pixels1++;
                else if (worst == 2) pixels2++;
                else pixelsMore++;
                bool hit = worst >= 1;
                mark[y * width + x] = hit;
                if (hit) marked++;
                float shown = hit ? 1f : 0f;
                error[e] = shown;
                error[e + 1] = shown;
                error[e + 2] = shown;
                error[e + 3] = 1f;
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mark[y * width + x]) continue;
                if (x + 1 < width && mark[y * width + x + 1]) step1++;
                if (x + 4 < width && mark[y * width + x + 4]) step4x++;
                if (y + 4 < height && mark[(y + 4) * width + x]) step4y++;
            }
        }

        stats.AppendLine("максимум уровней: " + maxLevel);
        stats.AppendLine("пиксели  0: " + pixels0 + "   1: " + pixels1 + "   2: " + pixels2 + "   3+: " + pixelsMore + "   из " + count);
        stats.AppendLine("каналы   0: " + channels0 + "   1: " + channels1 + "   2: " + channels2 + "   3+: " + channelsMore);
        if (marked > 0)
        {
            stats.AppendLine(
                "среди пикселей с разницей сосед через 1 px тоже с разницей: " + step1 +
                " из " + marked + "; через 4 px по X: " + step4x + "; через 4 px по Y: " + step4y);
        }
        return error;
    }

    static int ChannelDelta(float unity, float client)
    {
        int delta = ToByte(unity) - ToByte(client);
        return delta < 0 ? -delta : delta;
    }

    static int ToByte(float value)
    {
        return Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }

    static void CountChannel(int delta, ref int zero, ref int one, ref int two, ref int more)
    {
        if (delta == 0) zero++;
        else if (delta == 1) one++;
        else if (delta == 2) two++;
        else more++;
    }

    static void AppendUvNote(StringBuilder stats, string folder)
    {
        stats.AppendLine("Unity читает s0, s1 и s3 одним экранным UV 0..1.");
        string path = Path.Combine(folder, "uvs.txt");
        if (!File.Exists(path))
        {
            stats.AppendLine("uvs.txt нет, фактический v3 этого draw не записан.");
            return;
        }
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.StartsWith("vertex="))
            {
                stats.AppendLine(line);
            }
        }
        stats.AppendLine("tex0 и tex1 — UV сцены и блика. tex3 — UV дизера, это не экранный UV.");
    }

    static Texture2D MakeTexture(float[] pixels, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixelData(pixels, 0);
        texture.Apply(false, false);
        return texture;
    }

    static float[] LoadRaw(string path, Dictionary<string, string> meta, out int width, out int height)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Нет файла " + path);
        }

        width = IntValue(meta, "width");
        height = IntValue(meta, "height");
        int stride = IntValue(meta, "stride");
        string order = Value(meta, "memoryOrder");
        if (order == "unknown" && Value(meta, "format") == "31")
        {
            order = "A2B10G10R10";
        }
        if (width <= 0 || height <= 0 || stride <= 0)
        {
            throw new InvalidOperationException("В meta нет width/height/stride: " + path);
        }

        byte[] file = File.ReadAllBytes(path);
        if (file.Length < (long)stride * height)
        {
            throw new InvalidOperationException("Файл короче stride*height: " + path);
        }

        int bpp = BytesPerPixel(order);
        var pixels = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int fileY = height - 1 - y;
            int row = fileY * stride;
            for (int x = 0; x < width; x++)
            {
                int dst = (y * width + x) * 4;
                DecodePixel(file, row + x * bpp, order, pixels, dst);
            }
        }
        return pixels;
    }

    static void DecodePixel(byte[] file, int offset, string order, float[] dst, int dstOffset)
    {
        switch (order)
        {
            case "B8G8R8A8":
                dst[dstOffset] = file[offset + 2] / 255f;
                dst[dstOffset + 1] = file[offset + 1] / 255f;
                dst[dstOffset + 2] = file[offset] / 255f;
                dst[dstOffset + 3] = file[offset + 3] / 255f;
                break;
            case "R8G8B8A8":
                dst[dstOffset] = file[offset] / 255f;
                dst[dstOffset + 1] = file[offset + 1] / 255f;
                dst[dstOffset + 2] = file[offset + 2] / 255f;
                dst[dstOffset + 3] = file[offset + 3] / 255f;
                break;
            case "A2B10G10R10":
            {
                uint packed = BitConverter.ToUInt32(file, offset);
                dst[dstOffset] = (packed & 0x3FFu) / 1023f;
                dst[dstOffset + 1] = ((packed >> 10) & 0x3FFu) / 1023f;
                dst[dstOffset + 2] = ((packed >> 20) & 0x3FFu) / 1023f;
                dst[dstOffset + 3] = ((packed >> 30) & 0x3u) / 3f;
                break;
            }
            case "R16G16B16A16F":
                dst[dstOffset] = HalfToFloat(BitConverter.ToUInt16(file, offset));
                dst[dstOffset + 1] = HalfToFloat(BitConverter.ToUInt16(file, offset + 2));
                dst[dstOffset + 2] = HalfToFloat(BitConverter.ToUInt16(file, offset + 4));
                dst[dstOffset + 3] = HalfToFloat(BitConverter.ToUInt16(file, offset + 6));
                break;
            case "R16G16B16A16":
                dst[dstOffset] = BitConverter.ToUInt16(file, offset) / 65535f;
                dst[dstOffset + 1] = BitConverter.ToUInt16(file, offset + 2) / 65535f;
                dst[dstOffset + 2] = BitConverter.ToUInt16(file, offset + 4) / 65535f;
                dst[dstOffset + 3] = BitConverter.ToUInt16(file, offset + 6) / 65535f;
                break;
            case "R32G32B32A32F":
                dst[dstOffset] = BitConverter.ToSingle(file, offset);
                dst[dstOffset + 1] = BitConverter.ToSingle(file, offset + 4);
                dst[dstOffset + 2] = BitConverter.ToSingle(file, offset + 8);
                dst[dstOffset + 3] = BitConverter.ToSingle(file, offset + 12);
                break;
            default:
                throw new InvalidOperationException("Неизвестный memoryOrder " + order);
        }
    }

    static int BytesPerPixel(string order)
    {
        switch (order)
        {
            case "B8G8R8A8":
            case "R8G8B8A8":
            case "A2B10G10R10":
                return 4;
            case "R16G16B16A16F":
            case "R16G16B16A16":
                return 8;
            case "R32G32B32A32F":
                return 16;
            default:
                throw new InvalidOperationException("Неизвестный memoryOrder " + order);
        }
    }

    static float HalfToFloat(ushort value)
    {
        uint sign = (uint)(value & 0x8000) << 16;
        uint exp = (uint)((value >> 10) & 0x1F);
        uint mant = (uint)(value & 0x3FF);
        uint bits;
        if (exp == 0)
        {
            if (mant == 0)
            {
                bits = sign;
            }
            else
            {
                exp = 1;
                while ((mant & 0x400) == 0)
                {
                    mant <<= 1;
                    exp--;
                }
                mant &= 0x3FF;
                bits = sign | ((exp + (127 - 15)) << 23) | (mant << 13);
            }
        }
        else if (exp == 31)
        {
            bits = sign | 0x7F800000u | (mant << 13);
        }
        else
        {
            bits = sign | ((exp + (127 - 15)) << 23) | (mant << 13);
        }
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    static Dictionary<string, string> ReadMeta(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return map;
        }
        foreach (string raw in File.ReadAllLines(path))
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            map[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).Trim();
        }
        return map;
    }

    static Dictionary<string, string> ReadConstants(string path)
    {
        return ReadMeta(path);
    }

    static string Value(Dictionary<string, string> map, string key)
    {
        return map != null && map.TryGetValue(key, out string value) ? value : "";
    }

    static int IntValue(Dictionary<string, string> map, string key)
    {
        return int.TryParse(Value(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    static Vector4 VectorOf(Dictionary<string, string> map, string key, Vector4 fallback)
    {
        string text = Value(map, key);
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }
        string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return fallback;
        }
        return new Vector4(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]));
    }

    static float ParseFloat(string text)
    {
        return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static void AppendConstant(StringBuilder text, Dictionary<string, string> constants, string key)
    {
        string value = Value(constants, key);
        if (!string.IsNullOrEmpty(value))
        {
            text.AppendLine(key + " = " + value);
        }
    }

    static string ResolveDrawFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return null;
        }
        if (File.Exists(Path.Combine(path, "output.raw")))
        {
            return path;
        }

        string best = null;
        foreach (string child in Directory.GetDirectories(path))
        {
            if (!File.Exists(Path.Combine(child, "output.raw")))
            {
                continue;
            }
            string statePath = Path.Combine(child, "state.txt");
            if (File.Exists(statePath) && File.ReadAllText(statePath).Contains("combine=1"))
            {
                return child;
            }
            if (best == null)
            {
                best = child;
            }
        }
        return best;
    }

    static bool TryLatestDraw(out string folder)
    {
        folder = null;
        if (!Directory.Exists(GameCaptures))
        {
            return false;
        }
        string[] stamps = Directory.GetDirectories(GameCaptures);
        Array.Sort(stamps, StringComparer.Ordinal);
        for (int i = stamps.Length - 1; i >= 0; i--)
        {
            if (string.Equals(Path.GetFileName(stamps[i]), "unity_live", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string draw = ResolveDrawFolder(stamps[i]);
            if (!string.IsNullOrEmpty(draw))
            {
                folder = draw;
                return true;
            }
        }
        return false;
    }

    static bool TryLatestUnity(out string folder)
    {
        folder = null;
        string root = Path.Combine(GameCaptures, "unity_live");
        string pointer = Path.Combine(root, "latest.txt");
        if (File.Exists(pointer))
        {
            string pointed = File.ReadAllText(pointer).Trim();
            if (File.Exists(Path.Combine(pointed, "input.raw")))
            {
                folder = pointed;
                return true;
            }
        }
        if (!Directory.Exists(root))
        {
            return false;
        }
        string[] stamps = Directory.GetDirectories(root);
        Array.Sort(stamps, StringComparer.Ordinal);
        for (int i = stamps.Length - 1; i >= 0; i--)
        {
            if (File.Exists(Path.Combine(stamps[i], "input.raw")))
            {
                folder = stamps[i];
                return true;
            }
        }
        return false;
    }

    void CompareInputs()
    {
        if (!TryLatestDraw(out string clientFolder) || !TryLatestUnity(out string unityFolder))
        {
            _report = "Нужны оба снимка: draw клиента с output.raw и живой снимок Unity (клавиша G в Play Mode).";
            return;
        }

        var text = new StringBuilder();
        text.AppendLine("Клиент " + clientFolder);
        text.AppendLine("Unity " + unityFolder);
        text.AppendLine("Полосы — доли кадра, не одни и те же поверхности. Камеры могут стоять по-разному.");
        AppendFolderBands(text, "Клиент s0", clientFolder, "input");
        AppendFolderBands(text, "Unity s0", unityFolder, "input");
        AppendFolderBands(text, "Клиент после combine", clientFolder, "output");
        AppendFolderBands(text, "Unity после combine", unityFolder, "output");
        _report = text.ToString();
        _folder = unityFolder;
        Repaint();
    }

    void CompareCurve()
    {
        if (!TrySceneToFloat(out string folder))
        {
            _report = "Нет draw с role=scene-to-float. Это отдельный проход перед combine.";
            return;
        }

        Dictionary<string, string> inputMeta = ReadMeta(Path.Combine(folder, "input.meta.txt"));
        Dictionary<string, string> outputMeta = ReadMeta(Path.Combine(folder, "output.meta.txt"));
        float[] input = LoadRaw(Path.Combine(folder, "input.raw"), inputMeta, out int width, out int height);
        float[] output = LoadRaw(Path.Combine(folder, "output.raw"), outputMeta, out int outWidth, out int outHeight);
        int compareWidth = Math.Min(width, outWidth);
        int compareHeight = Math.Min(height, outHeight);
        double maxAbs = 0;
        double sum = 0;
        int count = 0;
        int over = 0;
        for (int y = 0; y < compareHeight; y++)
        {
            for (int x = 0; x < compareWidth; x++)
            {
                int iIn = (y * width + x) * 4;
                int iOut = (y * outWidth + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    float got = SceneToFloat(input[iIn + c]);
                    float expected = output[iOut + c];
                    double abs = Math.Abs(got - expected);
                    if (abs > maxAbs)
                    {
                        maxAbs = abs;
                    }
                    if (abs > 0.0005)
                    {
                        over++;
                    }
                    sum += abs;
                    count++;
                }
            }
        }

        var text = new StringBuilder();
        text.AppendLine(folder);
        text.AppendLine("Кривая 0A08B2C83DB6048D на сохранённом 8-битном входе, сверка с half-float выходом.");
        text.AppendLine("Каналов " + count + "  max abs " + maxAbs.ToString("0.000000") + "  среднее " + (count > 0 ? sum / count : 0).ToString("0.000000"));
        text.AppendLine("Каналов с |разницей| > 0.0005: " + over);
        _report = text.ToString();
        _folder = folder;
        Repaint();
    }

    static bool TrySceneToFloat(out string folder)
    {
        folder = null;
        if (!Directory.Exists(GameCaptures))
        {
            return false;
        }

        string[] stamps = Directory.GetDirectories(GameCaptures);
        Array.Sort(stamps, StringComparer.Ordinal);
        for (int i = stamps.Length - 1; i >= 0; i--)
        {
            if (string.Equals(Path.GetFileName(stamps[i]), "unity_live", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] draws = Directory.GetDirectories(stamps[i]);
            Array.Sort(draws, StringComparer.Ordinal);
            for (int d = 0; d < draws.Length; d++)
            {
                string statePath = Path.Combine(draws[d], "state.txt");
                if (!File.Exists(statePath) || !File.Exists(Path.Combine(draws[d], "input.raw")) || !File.Exists(Path.Combine(draws[d], "output.raw")))
                {
                    continue;
                }

                if (File.ReadAllText(statePath).Contains("role=scene-to-float"))
                {
                    folder = draws[d];
                    return true;
                }
            }
        }

        return false;
    }

    static float SceneToFloat(float scene)
    {
        const float c0x = -1.96000004f;
        const float c0w = 5f;
        float r1 = scene + c0x;
        float r2 = scene;
        r1 = r1 * r2 + 1f;
        r2 = Mathf.Sqrt(r1);
        return (scene + r2 - 1f) * c0w;
    }

    static void AppendFolderBands(StringBuilder text, string title, string folder, string name)
    {
        Dictionary<string, string> meta = ReadMeta(Path.Combine(folder, name + ".meta.txt"));
        float[] pixels = LoadRaw(Path.Combine(folder, name + ".raw"), meta, out int width, out int height);
        AppendSourceBands(text, title, pixels, width, height);
    }

    static void AppendSourceBands(StringBuilder stats, string title, float[] pixels, int width, int height)
    {
        int above = 0;
        int near = 0;
        int atMax = 0;
        int count = width * height;
        var bands = new System.Collections.Generic.List<float>[3, 3];
        for (int b = 0; b < 3; b++)
        {
            for (int c = 0; c < 3; c++)
            {
                bands[b, c] = new System.Collections.Generic.List<float>(count / 3 + 8);
            }
        }

        for (int y = 0; y < height; y++)
        {
            int band = y < height / 3 ? 0 : (y < 2 * height / 3 ? 1 : 2);
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                float r = pixels[i];
                float g = pixels[i + 1];
                float b = pixels[i + 2];
                float peak = Mathf.Max(r, Mathf.Max(g, b));
                if (peak > 1.001f)
                {
                    above++;
                }
                if (peak < 1f / 255f)
                {
                    near++;
                }
                if (ToByte(r) >= 254 || ToByte(g) >= 254 || ToByte(b) >= 254)
                {
                    atMax++;
                }
                bands[band, 0].Add(r);
                bands[band, 1].Add(g);
                bands[band, 2].Add(b);
            }
        }

        stats.AppendLine(title + " " + width + "x" + height);
        stats.AppendLine("канал > 1: " + above + "   пик < 1/255: " + near + "   канал >= 254: " + atMax + "   из " + count);
        string[] names = { "низ", "середина", "верх" };
        for (int b = 0; b < 3; b++)
        {
            stats.AppendLine(names[b] + " медиана RGB " +
                Median(bands[b, 0]).ToString("0.####") + " " +
                Median(bands[b, 1]).ToString("0.####") + " " +
                Median(bands[b, 2]).ToString("0.####"));
        }
    }

    static float[] BuildPairDelta(float[] withGlare, float[] blackGlare, int width, int height, StringBuilder stats)
    {
        var error = new float[width * height * 4];
        int pixels0 = 0, pixels1 = 0, pixels2 = 0, pixelsMore = 0;
        int[] ge2 = { 0, 0, 0 };
        int[] ge7 = { 0, 0, 0 };
        int[] bandCount = { 0, 0, 0 };
        for (int y = 0; y < height; y++)
        {
            int band = y < height / 3 ? 0 : (y < 2 * height / 3 ? 1 : 2);
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                int dr = Math.Abs(ToByte(withGlare[i]) - ToByte(blackGlare[i]));
                int dg = Math.Abs(ToByte(withGlare[i + 1]) - ToByte(blackGlare[i + 1]));
                int db = Math.Abs(ToByte(withGlare[i + 2]) - ToByte(blackGlare[i + 2]));
                int worst = Math.Max(dr, Math.Max(dg, db));
                if (worst == 0) pixels0++;
                else if (worst == 1) pixels1++;
                else if (worst == 2) pixels2++;
                else pixelsMore++;
                if (worst >= 2) ge2[band]++;
                if (worst >= 7) ge7[band]++;
                bandCount[band]++;
                float shown = worst >= 2 ? 1f : (worst == 1 ? 0.25f : 0f);
                error[i] = shown;
                error[i + 1] = shown;
                error[i + 2] = shown;
                error[i + 3] = 1f;
            }
        }

        stats.AppendLine("Вклад s1, клиентский s0, уровни A минус чёрный s1:");
        stats.AppendLine("пиксели  0: " + pixels0 + "   1: " + pixels1 + "   2: " + pixels2 + "   3+: " + pixelsMore);
        string[] names = { "низ", "середина", "верх" };
        for (int b = 0; b < 3; b++)
        {
            stats.AppendLine(names[b] + "  разница >= 2: " + ge2[b] + "   >= 7: " + ge7[b] + "   из " + bandCount[b]);
        }
        return error;
    }

    static float Median(System.Collections.Generic.List<float> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0f;
        }
        values.Sort();
        return values[values.Count / 2];
    }

    static void DestroyPreview(ref Texture2D texture)
    {
        if (texture != null)
        {
            DestroyImmediate(texture);
            texture = null;
        }
    }
}
