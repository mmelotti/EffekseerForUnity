﻿using System;
using System.IO;
using System.Text;
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Effekseer.Internal
{
	[Serializable]
	public class EffekseerMaterialResource
	{
		[SerializeField]
		public string path;
		[SerializeField]
		public EffekseerMaterialAsset asset;
			
#if UNITY_EDITOR
		public static EffekseerMaterialResource LoadAsset(string dirPath, string resPath) {
			resPath = Path.ChangeExtension(resPath, ".asset");

			EffekseerMaterialAsset asset = AssetDatabase.LoadAssetAtPath<EffekseerMaterialAsset>(EffekseerEffectAsset.NormalizeAssetPath(dirPath + "/" + resPath));

			var res = new EffekseerMaterialResource();
			res.path = resPath;
			res.asset = asset;
			return res;
		}
		public static bool InspectorField(EffekseerMaterialResource res) {
			EditorGUILayout.LabelField(res.path);
			var result = EditorGUILayout.ObjectField(res.asset, typeof(EffekseerMaterialAsset), false) as EffekseerMaterialAsset;
			if (result != res.asset) {
				res.asset = result;
				return true;
			}
			return false;
		}
#endif
	};
}

namespace Effekseer
{
	public class EffekseerMaterialAsset : ScriptableObject
	{
		public struct TextureProperty
		{
			[SerializeField]
			public string Name;
		}
		public struct UniformProperty
		{
			[SerializeField]
			public string Name;

			[SerializeField]
			public int Count;
		}

		public class ImportingAsset
		{
			public byte[] Data = new byte[0];
			public string Code = string.Empty;
			public bool IsCacheFile = false;
			public int UserTextureSlotMax = 6;
			public List<TextureProperty> Textures = new List<TextureProperty>();
			public List<UniformProperty> Uniforms = new List<UniformProperty>();
		}

		[SerializeField]
		public byte[] materialBuffers;

		[SerializeField]
		public byte[] cachedMaterialBuffers;

		[SerializeField]
		public Shader shader = null;

		[SerializeField]
		public List<TextureProperty> textures = new List<TextureProperty>();

		[SerializeField]
		public List<UniformProperty> uniforms = new List<UniformProperty>();

#if UNITY_EDITOR
		/// <summary>
		/// to avoid unity bug
		/// </summary>
		/// <param name="path"></param>
		public void AttachShader(string path)
		{
			if (this.shader != null) return;

			var resource = AssetDatabase.LoadAssetAtPath<Shader>(Path.ChangeExtension(path, ".shader"));
			this.shader = resource;
			EditorUtility.SetDirty(this);
			AssetDatabase.Refresh();
		}
		public static void CreateAsset(string path, ImportingAsset importingAsset)
		{
			string assetPath = Path.ChangeExtension(path, ".asset");

			var asset = AssetDatabase.LoadAssetAtPath<EffekseerMaterialAsset>(assetPath);
			if (asset != null)
			{
			}

			string assetDir = assetPath.Substring(0, assetPath.LastIndexOf('/'));

			if (asset == null)
			{
				asset = CreateInstance<EffekseerMaterialAsset>();
			}

			if(importingAsset.IsCacheFile)
			{
				asset.cachedMaterialBuffers = importingAsset.Data;
			}
			else
			{
				asset.materialBuffers = importingAsset.Data;
				asset.uniforms = importingAsset.Uniforms;
				asset.textures = importingAsset.Textures;
				asset.shader = CreateShader(Path.ChangeExtension(path, ".shader"), importingAsset);
			}

			AssetDatabase.CreateAsset(asset, assetPath);

			AssetDatabase.Refresh();
		}

		static string CreateMainShaderCode(ImportingAsset importingAsset, int stage)
		{
			var baseCode = importingAsset.Code;
			baseCode = baseCode.Replace("$F1$", "float");
			baseCode = baseCode.Replace("$F2$", "float2");
			baseCode = baseCode.Replace("$F3$", "float3");
			baseCode = baseCode.Replace("$F4$", "float4");
			baseCode = baseCode.Replace("$TIME$", "_Time.y");
			baseCode = baseCode.Replace("$UV$", "uv");
			baseCode = baseCode.Replace("$MOD", "fmod");

			int actualTextureCount = Math.Min(importingAsset.UserTextureSlotMax, importingAsset.Textures.Count);

			for (int i = 0; i < actualTextureCount; i++)
			{
				var keyP = "$TEX_P" + i + "$";
				var keyS = "$TEX_S" + i + "$";


				if (stage == 0)
				{
					baseCode = baseCode.Replace(
									   keyP,
									   "tex2Dlod(" + importingAsset.Textures[i].Name + ",GetUV(");
					baseCode =baseCode.Replace(keyS, "))");
				}
				else
				{
					baseCode = baseCode.Replace(
									   keyP,
									   "tex2D(" + importingAsset.Textures[i].Name + ",GetUV(");
					baseCode = baseCode.Replace(keyS, "))");
				}

			}

			// invalid texture
			for (int i = actualTextureCount; i < importingAsset.Textures.Count; i++)
			{
				var keyP = "$TEX_P" + i + "$";
				var keyS = "$TEX_S" + i + "$";
				baseCode = baseCode.Replace(keyP, "float4(");
				baseCode = baseCode.Replace(keyS, ",0.0,1.0)");
			}

			return baseCode;
		}
		static Shader CreateShader(string path, ImportingAsset importingAsset)
		{
			var nl = Environment.NewLine;
			var mainVSCode = CreateMainShaderCode(importingAsset, 0);
			var mainPSCode = CreateMainShaderCode(importingAsset, 1);

			var code = shaderTemplate;

			string codeProperty = string.Empty;
			string codeVariable = string.Empty;

			int actualTextureCount = Math.Min(importingAsset.UserTextureSlotMax, importingAsset.Textures.Count);

			for (int i = 0; i < actualTextureCount; i++)
			{
				codeProperty += importingAsset.Textures[i].Name + @"(""Color (RGBA)"", 2D) = ""white"" {}" + nl;
				codeVariable += "sampler2D " + importingAsset.Textures[i].Name + ";" + nl;
			}

			code = code.Replace("%TEX_PROPERTY%", codeProperty);
			code = code.Replace("%TEX_VARIABLE%", codeVariable);
			code = code.Replace("%VSCODE%", mainVSCode);
			code = code.Replace("%PSCODE%", mainPSCode);
			code = code.Replace("%MATERIAL_NAME%", System.IO.Path.GetFileNameWithoutExtension(path));

			AssetDatabase.StartAssetEditing();

			using (var writer = new StreamWriter(path))
			{
				writer.Write(code);
			}

			AssetDatabase.StopAssetEditing();

			AssetDatabase.Refresh(ImportAssetOptions.ImportRecursive);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ImportRecursive);
			
			var asset = AssetDatabase.LoadAssetAtPath<Shader>(path);
			return asset;
		}

		const string shaderTemplate = @"

Shader ""EffekseerMaterial/%MATERIAL_NAME%"" {

Properties{
	[Enum(UnityEngine.Rendering.BlendMode)] _BlendSrc(""Blend Src"", Float) = 0
	[Enum(UnityEngine.Rendering.BlendMode)] _BlendDst(""Blend Dst"", Float) = 0
	_BlendOp(""Blend Op"", Float) = 0
	_Cull(""Cull"", Float) = 0
	[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest(""ZTest Mode"", Float) = 0
	[Toggle] _ZWrite(""ZWrite"", Float) = 0

	%TEX_PROPERTY%
}

SubShader{

Blend[_BlendSrc][_BlendDst]
BlendOp[_BlendOp]
ZTest[_ZTest]
ZWrite[_ZWrite]
Cull[_Cull]

	Pass {

		CGPROGRAM

		#pragma target 5.0
		#pragma vertex vert
		#pragma fragment frag

		#include ""UnityCG.cginc""

		%TEX_VARIABLE%

		struct Vertex
		{
			float3 Pos;
			float4 Color;
			float3 Normal;
			float3 Tangent;
			float2 UV1;
			float2 UV2;
			// Custom1
			// Custom2
		};

		StructuredBuffer<Vertex> buf_vertex;
		float buf_offset;

		struct ps_input
		{
			float4 Position		: SV_POSITION;
			float4 VColor		: COLOR;
			float2 UV1		: TEXCOORD0;
			float2 UV2		: TEXCOORD1;
			float3 WorldP	: TEXCOORD2;
			float3 WorldN : TEXCOORD3;
			float3 WorldT : TEXCOORD4;
			float3 WorldB : TEXCOORD5;
			float2 ScreenUV : TEXCOORD6;
			//$C_OUT1$
			//$C_OUT2$
		};

		float2 GetUV(float2 uv)
		{
			uv.y = 1.0 - uv.y;
			return uv;
		}
		
		float2 GetUVBack(float2 uv)
		{
			uv.y = 1.0 - uv.y;
			return uv;
		}

		ps_input vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
		{
			int qind = (id) / 6;
			int vind = (id) % 6;

			int v_offset[6];
			v_offset[0] = 2;
			v_offset[1] = 1;
			v_offset[2] = 0;
			v_offset[3] = 1;
			v_offset[4] = 2;
			v_offset[5] = 3;

			Vertex Input = buf_vertex[buf_offset + qind * 4 + v_offset[vind]];

			ps_input Output;
			float3 worldPos = Input.Pos;
			float3 worldNormal = Input.Normal;
			float3 worldTangent = Input.Tangent;
			float3 worldBinormal = cross(worldNormal, worldTangent);
		
			// UV
			float2 uv1 = Input.UV1;
			float2 uv2 = Input.UV2;
			//uv1.y = mUVInversed.x + mUVInversed.y * uv1.y;
			//uv2.y = mUVInversed.x + mUVInversed.y * uv2.y;
		
			// NBT
			Output.WorldN = worldNormal;
			Output.WorldB = worldBinormal;
			Output.WorldT = worldTangent;
		
			float3 pixelNormalDir = worldNormal;
			float4 vcolor = Input.Color;

			%VSCODE%

			worldPos = worldPos + worldPositionOffset;

			// Unity Ext
			float4 cameraPos = mul(UNITY_MATRIX_V, float4(worldPos, 1.0f));
			cameraPos = cameraPos / cameraPos.w;
			Output.Position = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0f));
		
			Output.WorldP = worldPos;
			Output.VColor = Input.Color;
			Output.UV1 = uv1;
			Output.UV2 = uv2;
			Output.ScreenUV = Output.Position.xy / Output.Position.w;
			Output.ScreenUV.xy = float2(Output.ScreenUV.x + 1.0, 1.0 - Output.ScreenUV.y) * 0.5;
		
			return Output;
		}
		
		#ifdef _MATERIAL_LIT_
		
		#define lightScale 3.14
		
		float calcD_GGX(float roughness, float dotNH)
		{
			float alpha = roughness*roughness;
			float alphaSqr = alpha*alpha;
			float pi = 3.14159;
			float denom = dotNH * dotNH *(alphaSqr-1.0) + 1.0;
			return (alpha / denom) * (alpha / denom) / pi;
		}
		
		float calcF(float F0, float dotLH)
		{
			float dotLH5 = pow(1.0-dotLH,5.0);
			return F0 + (1.0-F0)*(dotLH5);
		}
		
		float calcG_Schlick(float roughness, float dotNV, float dotNL)
		{
			// UE4
			float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
			// float k = roughness * roughness / 2.0;
		
			float gV = dotNV*(1.0 - k) + k;
			float gL = dotNL*(1.0 - k) + k;
		
			return 1.0 / (gV * gL);
		}
		
		float calcLightingGGX(float3 N, float3 V, float3 L, float roughness, float F0)
		{
			float3 H = normalize(V+L);
		
			float dotNL = saturate( dot(N,L) );
			float dotLH = saturate( dot(L,H) );
			float dotNH = saturate( dot(N,H) ) - 0.001;
			float dotNV = saturate( dot(N,V) ) + 0.001;
		
			float D = calcD_GGX(roughness, dotNH);
			float F = calcF(F0, dotLH);
			float G = calcG_Schlick(roughness, dotNV, dotNL);
		
			return dotNL * D * F * G / 4.0;
		}
		
		float3 calcDirectionalLightDiffuseColor(float3 diffuseColor, float3 normal, float3 lightDir, float ao)
		{
			float3 color = float3(0.0,0.0,0.0);
		
			float NoL = dot(normal,lightDir);
			color.xyz = lightColor.xyz * lightScale * max(NoL,0.0) * ao / 3.14;
			color.xyz = color.xyz * diffuseColor.xyz;
			return color;
		}
		
		#endif
		
		float4 frag(ps_input Input) : COLOR
		{

			float2 uv1 = Input.UV1;
			float2 uv2 = Input.UV2;
			float3 worldPos = Input.WorldP;
			float3 worldNormal = Input.WorldN;
			float3 worldBinormal = Input.WorldB;
			float3 worldTangent = Input.WorldT;
		
			float3 pixelNormalDir = worldNormal;
			float4 vcolor = Input.VColor;
		
			%PSCODE%

			/*
			float3 viewDir = normalize(cameraPosition.xyz - worldPos);
			float3 diffuse = calcDirectionalLightDiffuseColor(baseColor, pixelNormalDir, lightDirection.xyz, ambientOcclusion);
			float3 specular = lightColor.xyz * lightScale * calcLightingGGX(worldNormal, viewDir, lightDirection.xyz, roughness, 0.9);
		
			float4 Output =  float4(metallic * specular + (1.0 - metallic) * diffuse, opacity);
			Output.xyz = Output.xyz + emissive.xyz;
		
			if(opacityMask <= 0.0) discard;
			if(opacity <= 0.0) discard;
		
			return Output;
			*/

			/*
			float airRefraction = 1.0;
			float3 dir = mul((float3x3)cameraMat, pixelNormalDir);
			dir.y = -dir.y;

			float2 distortUV = 	dir.xy * (refraction - airRefraction);

			distortUV += Input.ScreenUV;
			distortUV = GetUVBack(distortUV);	

			float4 bg = background_texture.Sample(background_sampler, distortUV);
			float4 Output = bg;

			if(opacityMask <= 0.0) discard;
			if(opacity <= 0.0) discard;

			return Output;
			*/

			float4 Output = float4(emissive, opacity);
		
			if(opacityMask <= 0.0f) discard;
			if(opacity <= 0.0) discard;
		
			return Output;
		}

		ENDCG
	}

}

Fallback Off

}

";

#endif
	}
}
