using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runeveil
{
    public sealed class RuneveilArena : MonoBehaviour
    {
        public enum Target { Miss, Body, Core }
        private Transform golem, core, ward, action, brackets, punchArcs;
        private Transform bossFill, statusPanel, handHud;
        private readonly MeshRenderer[] heartSlots=new MeshRenderer[3];
        private TextMeshPro actionText, statusText;
        private TMP_FontAsset font;
        private MeshRenderer wardChip;
        private Material hudMuted, hudAccent, emptyHeart;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly System.Random rng = new System.Random(83179);
        private Mesh beveledBlock, rockMesh, ballMesh, planeMesh, gemMesh;
        private Shader lit, unlit;
        private Material litSeed, unlitSeed, alphaSeed;
        private Material[] stones, moss;
        private Material ivory, amber, mint, contact, lampPool;
        private Material corePaint, punchPaint;
        private Material wardPaint, wardRim, wardRingPaint, impactPaint;
        private Transform wardRipple, floorRipple;
        private Mesh[] runeGlyphs, bolts;
        private Material[] spellBodies;
        private SpellFx[] spellFx;
        private int nextFx;
        private float wardBlend, wardFlash, impactFlash;
        private Arm raised, planted;
        private float time, lift, slam, flinch, slump;
        private readonly List<Transform> stoneHits = new List<Transform>();
        private readonly List<Transform> boxHits = new List<Transform>();

        private sealed class Arm
        {
            public Vector3 Shoulder;
            public Transform Upper, Lower, Fist;
        }

        private sealed class SpellFx
        {
            public Transform Root, Shot, Glyph, Hit;
            public Transform[] Debris;
            public Material RuneGlow, HitGlow;
            public int Spell;
            public float Age;
            public Vector3 From, To, Normal;
        }

        private static readonly uint[] spellColors={0xff855c,0xffda78,0x9cdeff,0xc59375,0xc4acff,0x97f6d3};
        private static readonly float[] spellTimes={.38f,.11f,.31f,.52f,.36f,.0f};
        private static readonly float[] spellArcs={.29f,.0f,.13f,.64f,.104f,.0f};

        private void Awake()
        {
            InitArt();
            BuildEnvironment();
            BuildCreature();
            BuildHud();
            BuildEffects();
            ResetView();
        }

        public static RuneveilArena Create(Transform parent = null)
        {
            var go = new GameObject("Runeveil Arena");
            go.transform.SetParent(parent,false);
            return go.AddComponent<RuneveilArena>();
        }

        public Target Aim(Ray ray, out Vector3 point)
        {
            Vector3 closest=default;
            float nearest=float.PositiveInfinity;
            void Consider(Transform body,bool block)
            {
                Ray local=new Ray(body.InverseTransformPoint(ray.origin),body.InverseTransformVector(ray.direction).normalized);
                float distance;
                if(block)
                {
                    if(!new Bounds(Vector3.zero,Vector3.one*1.94f).IntersectRay(local,out distance)) return;
                }
                else if(!IntersectSphere(local,Vector3.zero,.945f,out distance)) return;
                Vector3 world=body.TransformPoint(local.GetPoint(distance));
                float depth=Vector3.Distance(ray.origin,world);
                if(depth<nearest) {nearest=depth;closest=world;}
            }
            foreach(var body in stoneHits) Consider(body,false);
            foreach(var block in boxHits) Consider(block,true);
            if(IntersectSphere(ray,core.position,.315f,out var heart) && heart<=nearest+.12f)
            { point=ray.GetPoint(heart); return Target.Core; }
            if(!float.IsPositiveInfinity(nearest)) { point=closest;return Target.Body; }
            var floor=new Plane(transform.up,transform.position);
            point=ray.GetPoint(floor.Raycast(ray,out var ground)&&ground<9?ground:9f);
            return Target.Miss;
        }

        private static bool IntersectSphere(Ray ray,Vector3 center,float radius,out float distance)
        {
            Vector3 offset=ray.origin-center;
            float b=Vector3.Dot(offset,ray.direction);
            float discriminant=b*b-offset.sqrMagnitude+radius*radius;
            if(discriminant<0) { distance=0;return false; }
            float half=Mathf.Sqrt(discriminant);
            distance=-b-half;
            if(distance<0) distance=-b+half;
            return distance>=0;
        }

        public bool AimAction(Ray ray)
        {
            if(!action.gameObject.activeSelf) return false;
            var plane=new Plane(action.forward,action.position);
            if(!plane.Raycast(ray,out var distance)) return false;
            Vector3 local=action.InverseTransformPoint(ray.GetPoint(distance));
            return Mathf.Abs(local.x)<=.337f&&Mathf.Abs(local.y)<=.111f;
        }

        public void Present(float dt,float windup,bool shield,bool coreAimed,bool won=false,bool lost=false)
        {
            dt=Mathf.Clamp(dt,0,.075f);
            time+=dt;
            slam=Mathf.Max(0,slam-dt);
            flinch=Mathf.Max(0,flinch-dt*2.8f);
            slump=Approach(slump,won?1:0,4.7f,dt);
            bool warning=windup>=0&&!won&&!lost;
            lift=Approach(lift,warning?Mathf.SmoothStep(0,1,windup*1.35f):0,warning?8:6,dt);
            float punch=slam>0?Mathf.Sin(Mathf.PI*Mathf.Min(.99f,(1-slam/.52f)*1.46f))*(1-slump):0;
            golem.localPosition=new Vector3(0,Mathf.Sin(time*1.24f)*.017f-.018f-slump*.55f-punch*.12f,5.73f);
            golem.localRotation=Quaternion.Euler(0,-Mathf.Sin(time*.67f)*1.3f-punch*9.5f,
                Mathf.Sin(time*.9f)*.7f+flinch*Mathf.Sin(time*28)*2.7f-slump*8);
            MoveArm(raised,new Vector3(-1.45f-lift*.28f+punch*.22f,1.81f+lift*.48f-punch*.49f-slump*.17f,-.055f-lift*.21f-punch*.55f),
                new Vector3(-1.78f-lift*.15f+punch*.46f,2.28f+lift*.68f-punch*1.63f-slump*1.12f,-.624f-punch*.72f),
                new Vector3(-8+lift*16-punch*32,13,-14+lift*21+punch*10));
            MoveArm(planted,new Vector3(1.42f,1.38f+lift*.03f+slump*.053f,-.14f-lift*.11f),
                new Vector3(1.63f,.593f+lift*.055f+slump*.56f,-.88f-lift*.057f),new Vector3(-7+lift*9,-7-slump*14,-11));
            punchArcs.gameObject.SetActive(warning);
            if(warning)
            {
                punchArcs.localPosition=raised.Fist.localPosition+new Vector3(0,0,-.028f);
                punchArcs.localRotation=Quaternion.Euler(0,0,-lift*16);
                var charge=ColorHex(0xffb159); charge.a=.45f+.43f*windup; Tint(punchPaint,charge);
            }
            core.localScale=new Vector3(1,1,.92f)*(1+(warning?.072f+.03f*windup:0));
            float danger=warning?.67f+.33f*windup:coreAimed?.20f:0;
            Color fire=Color.Lerp(ColorHex(0xffa833),ColorHex(0xffe477),danger);
            Tint(corePaint,Color.Lerp(fire,ColorHex(0xa75e37),slump*.83f));
            brackets.gameObject.SetActive((warning||coreAimed)&&!won&&!lost);
            wardChip.sharedMaterial=shield?mint:hudMuted;
            UpdateEffects(dt,shield);
        }

        private static float Approach(float value,float target,float rate,float dt) => Mathf.Lerp(value,target,1-Mathf.Exp(-rate*dt));

        private static void MoveArm(Arm arm,Vector3 elbow,Vector3 wrist,Vector3 fistAngle)
        {
            Vector3 upper=elbow-arm.Shoulder,lower=wrist-elbow;
            arm.Upper.SetLocalPositionAndRotation(Vector3.Lerp(arm.Shoulder,elbow,.54f),Quaternion.FromToRotation(Vector3.up,upper));
            arm.Upper.localScale=new Vector3(1,Mathf.Max(.57f,upper.magnitude/.85f),1);
            arm.Lower.SetLocalPositionAndRotation(Vector3.Lerp(elbow,wrist,.43f),Quaternion.FromToRotation(Vector3.up,lower));
            arm.Lower.localScale=new Vector3(1,Mathf.Max(.55f,lower.magnitude/.76f),1);
            arm.Fist.SetLocalPositionAndRotation(wrist,Quaternion.Euler(fistAngle));
        }

        public void SetHud(int bossHP,int maxBossHP,int hearts,string status=null,string actionLabel=null)
        {
            float fraction=maxBossHP>0?Mathf.Clamp01(bossHP/(float)maxBossHP):0;
            bossFill.gameObject.SetActive(fraction>0);
            bossFill.localScale=new Vector3(1.99f*fraction,.054f,.014f);
            bossFill.localPosition=new Vector3((fraction-1)*.995f,0,-.027f);
            for(int i=0;i<heartSlots.Length;i++) heartSlots[i].sharedMaterial=i<hearts?ivory:emptyHeart;
            action.gameObject.SetActive(!string.IsNullOrEmpty(actionLabel));
            statusPanel.gameObject.SetActive(!string.IsNullOrWhiteSpace(status));
            FitText(actionText,actionLabel,.56f,.049f);
            FitText(statusText,status,1.17f,.049f);
        }

        public void SetHand(Transform view,Vector3? tip)
        {
            bool valid=view&&tip.HasValue&&(tip.Value-view.position).sqrMagnitude>.0625f;
            handHud.gameObject.SetActive(valid);
            if(!valid) return;
            Vector3 at=tip.Value-view.up*.096f+view.forward*.073f;
            handHud.SetPositionAndRotation(at,Quaternion.LookRotation(at-view.position,view.up));
        }

        public void Cast(string spell,Vector3 from,Vector3 to,Vector3 head,Vector3 forward,Target part=Target.Miss)
        {
            if(!Enum.TryParse<RuneSpell>(spell,true,out var rune)||(uint)rune>=(uint)spellColors.Length) return;
            if (rune==RuneSpell.Barrier)
            {
                Vector3 horizontal=Vector3.ProjectOnPlane(forward,Vector3.up);
                ward.SetPositionAndRotation(head-Vector3.up*1.6f,
                    Quaternion.LookRotation(horizontal.sqrMagnitude>.001f?horizontal:transform.forward));
                wardFlash=.73f;
            }
            else if(part!=Target.Miss) flinch=rune==RuneSpell.Lightning&&part==Target.Core?1f:.38f;
            var shot=spellFx[nextFx]; nextFx=(nextFx+1)%spellFx.Length;
            shot.Spell=(int)rune;
            shot.From=from; shot.To=to; shot.Age=0;
            shot.Normal=forward.sqrMagnitude>.001f?forward.normalized:transform.forward;
            shot.Root.gameObject.SetActive(true);
            shot.Glyph.GetComponent<MeshFilter>().sharedMesh=runeGlyphs[shot.Spell];
            shot.Shot.GetComponent<MeshFilter>().sharedMesh=bolts[shot.Spell];
            shot.Shot.GetComponent<MeshRenderer>().sharedMaterial=spellBodies[shot.Spell];
            AnimateSpell(shot,0);
        }
        public void Impact(bool blocked)
        {
            slam=.52f;impactFlash=blocked?.49f:1;
            if(blocked) wardFlash=1;
        }

        public void ResetView()
        {
            time=lift=slam=flinch=slump=wardBlend=wardFlash=impactFlash=0;
            nextFx=0;
            foreach(var shot in spellFx) shot.Root.gameObject.SetActive(false);
            ward.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            Present(0,-1,false,false);
        }

        private void InitArt()
        {
            litSeed=Resources.Load<Material>("Runeveil/RuneveilLit");
            unlitSeed=Resources.Load<Material>("Runeveil/RuneveilUnlit");
            alphaSeed=Resources.Load<Material>("Runeveil/RuneveilTransparent");
            lit = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            beveledBlock = RoundedBlock();
            rockMesh = Orb(13, 8, .073f);
            ballMesh = Orb(11, 7, 0);
            planeMesh = Profile("Quad", new Vector2(-.5f,-.5f), new Vector2(-.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,-.5f));
            gemMesh = Lathe(5, new Vector2(0,-.37f),new Vector2(.21f,-.17f),new Vector2(.253f,.11f),new Vector2(.172f,.265f), new Vector2(0,.378f));
            stones = new[] { Paint(0x99798b), Paint(0xa88491), Paint(0x896d80), Paint(0xb28d93), Paint(0x7d637e), Paint(0xa17b8c) };
            moss = new[] { Paint(0x8f9c46), Paint(0x72853e), Paint(0xa2a853) };
            ivory = Paint(0xffe5ba, true);
            amber = Paint(0xffba50, true);
            mint = Paint(0x9cf5d4, true);
            var spot = RadialTexture();
            contact = Paint(0x3a223a,true,.63f,spot);
            lampPool = Paint(0xffbb61,true,.27f,spot);
        }

        private T Keep<T>(T obj) where T : UnityEngine.Object { owned.Add(obj); return obj; }
        private static Color ColorHex(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        private Material Paint(uint rgb, bool flat = false, float alpha = 1, Texture map = null)
        {
            Material seed=alpha<1?alphaSeed:flat?unlitSeed:litSeed;
            var material = Keep(seed?new Material(seed):new Material(flat?unlit:lit));
            material.name = "Runeveil " + rgb.ToString("X6");
            Color color = ColorHex(rgb);
            color.a = alpha;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (map != null) material.mainTexture = map;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .17f);
            if (!flat && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .12f);
            }
            if (alpha < 1)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (int)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.SetFloat("_Cull", (int)CullMode.Off);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("DepthOnly", false);
            }
            return material;
        }

        private static void Tint(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private Texture2D RadialTexture()
        {
            const int size = 64;
            var texture = Keep(new Texture2D(size,size,TextureFormat.RGBA32,false));
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size*size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(x/(size-1f)*2-1,y/(size-1f)*2-1);
                float a = Mathf.Pow(Mathf.Max(0,1-point.sqrMagnitude),3);
                pixels[x+y*size] = new Color32(255,255,255,(byte)(a*255));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false,true);
            return texture;
        }

        private static Transform Group(Transform parent, string name, Vector3 at = default)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent,false);
            node.localPosition = at;
            return node;
        }

        private Transform Draw(Transform parent, Mesh mesh, Material material, Vector3 at, Vector3 scale, string name,
            Quaternion rotation = default)
        {
            var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            var pose = go.transform;
            pose.SetParent(parent,false);
            pose.SetLocalPositionAndRotation(at,rotation==default?Quaternion.identity:rotation);
            pose.localScale=scale;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;
            return pose;
        }

        private sealed class Batch
        {
            private readonly Dictionary<Material,List<CombineInstance>> parts = new Dictionary<Material,List<CombineInstance>>();
            public void Add(Mesh mesh,Material material,Vector3 at,Vector3 scale,Quaternion pose=default)
            {
                if (!parts.TryGetValue(material,out var group))
                {
                    group=new List<CombineInstance>();
                    parts.Add(material,group);
                }
                group.Add(new CombineInstance {mesh=mesh,transform=Matrix4x4.TRS(at,pose==default?Quaternion.identity:pose,scale)});
            }
            public void Place(RuneveilArena arena,Transform parent,string name)
            {
                foreach (var group in parts)
                {
                    var combined=arena.Keep(new Mesh {name=name,indexFormat=IndexFormat.UInt32});
                    combined.CombineMeshes(group.Value.ToArray());
                    arena.Draw(parent,combined,group.Key,Vector3.zero,Vector3.one,name);
                }
            }
        }

        private Mesh Geometry(string name,List<Vector3> vertices,List<int> triangles,List<Vector2> uv,List<Vector3> normals=null)
        {
            var mesh=Keep(new Mesh {name=name});
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles,0);
            mesh.SetUVs(0,uv);
            if (normals!=null) mesh.SetNormals(normals); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh Profile(string name,params Vector2[] outline)
        {
            var verts=new List<Vector3>();
            var indices=new List<int>();
            var uv=new List<Vector2>();
            for (int i=0;i<outline.Length;i++)
            {
                verts.Add(outline[i]); uv.Add(outline[i]+Vector2.one*.5f);
                if (i>1) { indices.Add(0); indices.Add(i-1); indices.Add(i); }
            }
            return Geometry(name,verts,indices,uv);
        }

        private Mesh Lathe(int sides,params Vector2[] profile)
        {
            var verts=new List<Vector3>();
            var indices=new List<int>();
            var uv=new List<Vector2>();
            for(int j=0;j<profile.Length;j++)
            for(int i=0;i<=sides;i++)
            {
                float angle=i/(float)sides*Mathf.PI*2;
                verts.Add(new Vector3(Mathf.Sin(angle)*profile[j].x,profile[j].y,Mathf.Cos(angle)*profile[j].x));
                uv.Add(new Vector2(i/(float)sides,j/(float)(profile.Length-1)));
                if(i==sides||j==profile.Length-1) continue;
                int a=j*(sides+1)+i;
                indices.Add(a); indices.Add(a+1); indices.Add(a+sides+1);
                indices.Add(a+1); indices.Add(a+sides+2); indices.Add(a+sides+1);
            }
            return Geometry("Turned profile",verts,indices,uv);
        }

        private Mesh Orb(int sides,int rows,float grain)
        {
            var profile=new Vector2[rows+1];
            for(int row=0;row<=rows;row++)
            {
                float phi=row/(float)rows*Mathf.PI;
                profile[row]=new Vector2(Mathf.Sin(phi),-Mathf.Cos(phi));
            }
            var mesh=Lathe(sides,profile);
            var verts=mesh.vertices;
            var normals=new Vector3[verts.Length];
            for(int i=0;i<verts.Length;i++)
            {
                Vector3 p=verts[i];
                normals[i]=p.normalized;
                verts[i]*=1+Mathf.Sin(p.x*8.1f+p.z*5.4f)*Mathf.Cos(p.y*9.3f-p.z*3.9f)*grain;
            }
            mesh.vertices=verts; mesh.normals=normals; mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh RoundedBlock()
        {
            var verts=new List<Vector3>(); var indices=new List<int>(); var uv=new List<Vector2>(); var normals=new List<Vector3>();
            float[] divisions={-1,-.86f,-.69f,.69f,.86f,1};
            foreach(Vector3 side in new[] {Vector3.forward,Vector3.back,Vector3.left,Vector3.right,Vector3.up,Vector3.down})
            {
                Vector3 h=Vector3.Cross(Mathf.Abs(side.y)>.9f?Vector3.forward:Vector3.up,side).normalized;
                Vector3 v=Vector3.Cross(side,h);
                int first=verts.Count;
                for(int j=0;j<divisions.Length;j++)
                for(int i=0;i<divisions.Length;i++)
                {
                    Vector3 raw=side+h*divisions[i]+v*divisions[j];
                    Vector3 inset=new Vector3(Mathf.Clamp(raw.x,-.81f,.81f),Mathf.Clamp(raw.y,-.81f,.81f),Mathf.Clamp(raw.z,-.81f,.81f));
                    Vector3 n=(raw-inset).normalized;
                    verts.Add(inset+n*.19f); normals.Add(n);
                    uv.Add(new Vector2(i/5f,j/5f));
                    if(i==5||j==5) continue;
                    int id=first+j*6+i;
                    indices.Add(id);indices.Add(id+1);indices.Add(id+6);
                    indices.Add(id+1);indices.Add(id+7);indices.Add(id+6);
                }
            }
            return Geometry("Rounded carved block",verts,indices,uv,normals);
        }

        private float Range(float min,float max) => Mathf.Lerp(min,max,(float)rng.NextDouble());
        private T Pick<T>(T[] options) => options[rng.Next(options.Length)];

        private void BuildHud()
        {
            font=Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")??TMP_Settings.defaultFontAsset;
            hudMuted=Paint(0x372638,true);
            hudAccent=Paint(0xb68571,true);
            emptyHeart=Paint(0x8d616d,true);
            var boss=Group(transform,"Golem health",new Vector3(0,3.85f,4.91f));
            Draw(boss,beveledBlock,hudAccent,Vector3.zero,new Vector3(2.11f,.124f,.027f)*.5f,"Gilded track");
            Draw(boss,beveledBlock,hudMuted,new Vector3(0,0,-.018f),new Vector3(2.066f,.089f,.027f)*.5f,"Health track");
            bossFill=Draw(boss,planeMesh,amber,new Vector3(0,0,-.027f),new Vector3(1.99f,.054f,1),"Golem health fill");
            var heading=Text(boss,"Mossbound Golem",new Vector3(0,.147f,-.023f),0xffe7bd);
            FitText(heading,"MOSSBOUND GOLEM",1.22f,.072f);
            var player=Group(transform,"Player hearts",new Vector3(-.76f,1.02f,2.05f));
            var heart=Breastplate();
            for(int i=0;i<heartSlots.Length;i++)
                heartSlots[i]=Draw(player,heart,ivory,new Vector3(i*.135f,0,0),new Vector3(.071f,.106f,.08f),"Life heart").GetComponent<MeshRenderer>();
            statusPanel=Group(transform,"Duel status",new Vector3(0,2.88f,3.38f));
            Draw(statusPanel,beveledBlock,hudMuted,Vector3.zero,new Vector3(.624f,.068f,.014f),"Status plate");
            statusText=Text(statusPanel,"Status",new Vector3(0,0,-.025f),0xffe7bd);
            action=Group(transform,"Duel action",new Vector3(-.56f,1.24f,1.82f));
            Draw(action,beveledBlock,hudAccent,Vector3.zero,new Vector3(.320f,.092f,.014f),"Action frame");
            Draw(action,beveledBlock,hudMuted,new Vector3(0,0,-.011f),new Vector3(.309f,.079f,.014f),"Action plate");
            actionText=Text(action,"Start or replay",new Vector3(0,0,-.031f),0xffe8c2);
            handHud=Group(transform,"Hand spell indicators");
            wardChip=HandChip(-.102f,"BARRIER",0x98f6d3).GetComponent<MeshRenderer>();
            HandChip(.102f,"ATTACK",0xffd081);
            handHud.gameObject.SetActive(false);
            SetHud(120,120,3,null,"START DUEL");
        }

        private Transform HandChip(float x,string name,uint color)
        {
            var chip=Group(handHud,name,new Vector3(x,0,0));
            Draw(chip,beveledBlock,Paint(color,true),Vector3.zero,new Vector3(.095f,.030f,.006f),"Spell chip edge");
            var fill=Draw(chip,beveledBlock,hudMuted,new Vector3(0,0,-.003f),new Vector3(.090f,.025f,.006f),"Spell chip fill");
            var text=Text(chip,name,new Vector3(0,0,-.012f),color);
            FitText(text,name,.151f,.019f);
            return fill;
        }

        private TextMeshPro Text(Transform parent,string name,Vector3 where,uint color)
        {
            var go=new GameObject(name,typeof(RectTransform));
            go.transform.SetParent(parent,false);
            go.transform.localPosition=where;
            var text=go.AddComponent<TextMeshPro>();
            if(font) text.font=font;
            text.fontSize=24;
            text.color=ColorHex(color);
            text.alignment=TextAlignmentOptions.Center;
            text.textWrappingMode=TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta=new Vector2(160,48);
            text.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            return text;
        }

        private static void FitText(TextMeshPro text,string value,float width,float height)
        {
            value=value??"";
            if(text.text==value) return;
            text.SetText(value);
            if(value.Length==0) return;
            text.ForceMeshUpdate(true,true);
            Vector3 bounds=text.textBounds.size;
            float scale=Mathf.Min(width/Mathf.Max(bounds.x,.01f),height/Mathf.Max(bounds.y,.01f));
            text.transform.localScale=Vector3.one*scale;
        }

        private void BuildEffects()
        {
            var stage=Group(transform,"Runes and impacts");
            ward=Group(stage,"Mint ward");
            wardPaint=Paint(0x89f0cb,true,.17f);
            wardRim=Paint(0xadf8dc,true,.79f);
            wardRingPaint=Paint(0xc0ffee,true,.66f);
            Draw(ward,ShieldShell(),wardPaint,new Vector3(0,.12f,.30f),new Vector3(1.85f,1.14f,1.48f),"Translucent shield shell");
            var rim=new Vector3[33];
            for(int i=0;i<rim.Length;i++)
            {
                float x=(i/(float)(rim.Length-1)*2-1)*.983f,r=Mathf.Sqrt(1-x*x);
                float theta=Mathf.Acos(.761f*r)-.155f;
                rim[i]=new Vector3(x*1.85f,.12f+Mathf.Cos(theta)*1.14f*r,.30f+Mathf.Sin(theta)*1.48f*r);
            }
            Draw(ward,Tube("Shield bright rim",rim,.020f),wardRim,Vector3.zero,Vector3.one,"Bright mint edge");
            var ripple=Tube("Ripple ring",Arc(.234f,.234f,0,350,31),.012f,true);
            wardRipple=Draw(ward,ripple,wardRingPaint,new Vector3(0,.73f,1.59f),Vector3.one,"Shield impact ripple");
            impactPaint=Paint(0xea9764,true,.62f);
            floorRipple=Draw(stage,ripple,impactPaint,new Vector3(-1.18f,.044f,4.39f),Vector3.one,"Slam ring on the paving",Quaternion.Euler(90,0,0));
            runeGlyphs=new Mesh[spellColors.Length];
            spellBodies=new Material[spellColors.Length];
            for(int i=0;i<spellColors.Length;i++)
            {
                runeGlyphs[i]=RuneMark((RuneSpell)i);
                spellBodies[i]=Paint(spellColors[i],i!=(int)RuneSpell.Rock);
            }
            bolts=new[]{ballMesh,Tube("Forked lightning",new[]{Vector3.zero,new Vector3(.139f,.175f,.087f),new Vector3(-.167f,.295f,-.098f),
                new Vector3(.14f,.424f,-.081f),new Vector3(-.179f,.61f,.074f),new Vector3(.148f,.754f,-.106f),new Vector3(-.075f,.865f,0),Vector3.up},.026f),
                gemMesh,rockMesh,Tube("Arc of wind",Arc(.238f,.290f,4,176,30),.024f),ballMesh};
            spellFx=new SpellFx[6];
            for(int i=0;i<spellFx.Length;i++)
            {
                var node=Group(stage,"Pooled rune cast");
                var pen=Paint(0xffffff,true,.91f);
                var burst=Paint(0xffffff,true,.82f);
                var impact=Group(node,"Elemental fragments");
                var debris=new Transform[7];
                for(int j=0;j<debris.Length;j++)
                    debris[j]=Draw(impact,gemMesh,burst,Vector3.zero,Vector3.one*.11f,"Shard");
                Draw(impact,ripple,burst,Vector3.zero,Vector3.one,"Arrival ripple");
                spellFx[i]=new SpellFx {
                    Root=node,
                    Glyph=Draw(node,runeGlyphs[0],pen,Vector3.zero,Vector3.one,"Hand glyph"),
                    Shot=Draw(node,bolts[0],spellBodies[0],Vector3.zero,Vector3.one,"Flying element"),
                    Hit=impact,Debris=debris,RuneGlow=pen,HitGlow=burst,
                };
            }
        }

        private Mesh ShieldShell()
        {
            const int rows=14,columns=35;
            var verts=new List<Vector3>(); var uv=new List<Vector2>(); var inds=new List<int>();
            for(int j=0;j<=rows;j++)
            for(int i=0;i<=columns;i++)
            {
                float latitude=j/(float)rows*Mathf.PI*.5f;
                float longitude=i/(float)columns*Mathf.PI;
                verts.Add(new Vector3(Mathf.Cos(longitude)*Mathf.Sin(latitude),Mathf.Cos(latitude),Mathf.Sin(longitude)*Mathf.Sin(latitude)));
                uv.Add(new Vector2(i/(float)columns,j/(float)rows));
                if(j==rows||i==columns) continue;
                int a=j*(columns+1)+i;
                inds.Add(a);inds.Add(a+columns+1);inds.Add(a+1);
                inds.Add(a+1);inds.Add(a+columns+1);inds.Add(a+columns+2);
            }
            return Geometry("Half sphere guard",verts,inds,uv);
        }

        private Mesh RuneMark(RuneSpell spell)
        {
            Vector3 P(float x,float y)=>new Vector3(x*.198f,y*.198f,0);
            Vector3[] shape;
            switch(spell)
            {
                case RuneSpell.Fire:
                    return Tube("Circle of fire",Arc(.099f,.099f,0,351,39),.006f,true);
                case RuneSpell.Lightning:
                    shape=new[]{P(-.5f,.49f),P(.5f,.49f),P(-.5f,-.49f),P(.5f,-.49f)}; break;
                case RuneSpell.Ice:
                    shape=new[]{P(-.5f,.49f),P(0,-.5f),P(.5f,.49f)}; break;
                case RuneSpell.Rock:
                    return Tube("Stone triangle",new[]{P(0,.5f),P(-.5f,-.45f),P(.5f,-.45f)},.006f,true);
                case RuneSpell.Wind:
                    shape=new Vector3[32];
                    for(int i=0;i<shape.Length;i++)
                    {
                        float t=i/(float)(shape.Length-1);
                        shape[i]=P(Mathf.Cos(t*Mathf.PI*2+.76f)*-.477f,.52f-t*1.06f);
                    }
                    break;
                default:
                    shape=new[]{P(-.54f,0),P(.54f,0)};break;
            }
            return Tube(spell+" rune",shape,.006f);
        }

        private void UpdateEffects(float dt,bool guarding)
        {
            wardBlend=Approach(wardBlend,guarding?1:0,19,dt);
            wardFlash=Mathf.Max(0,wardFlash-dt*2.7f);
            float show=Mathf.Max(wardBlend,wardFlash);
            ward.gameObject.SetActive(show>.016f);
            if(ward.gameObject.activeSelf)
            {
                Alpha(wardPaint,0x88eec8,show*.135f+wardFlash*.071f);
                Alpha(wardRim,0x9ff8d9,show*.68f+wardFlash*.15f);
                wardRipple.gameObject.SetActive(wardFlash>.04f);
                wardRipple.localScale=Vector3.one*(.45f+(1-wardFlash)*3.57f);
                Alpha(wardRingPaint,0xc1ffe9,wardFlash*.67f);
            }
            impactFlash=Mathf.Max(0,impactFlash-dt*2.45f);
            floorRipple.gameObject.SetActive(impactFlash>.02f);
            if(floorRipple.gameObject.activeSelf)
            {
                floorRipple.localScale=Vector3.one*(1.3f+(1-impactFlash)*5.7f);
                Alpha(impactPaint,0xe79464,impactFlash*.65f);
            }
            foreach(var cast in spellFx) if(cast.Root.gameObject.activeSelf) AnimateSpell(cast,dt);
        }

        private static void Alpha(Material material,uint rgb,float alpha)
        {
            Color color=ColorHex(rgb);color.a=alpha;Tint(material,color);
        }

        private void AnimateSpell(SpellFx shot,float dt)
        {
            shot.Age+=dt;
            int type=shot.Spell;
            bool barrier=type==(int)RuneSpell.Barrier;
            bool lightning=type==(int)RuneSpell.Lightning;
            float transit=spellTimes[type];
            float elapsed=shot.Age;
            bool alive=elapsed<(barrier?.26f:transit+.35f);
            shot.Root.gameObject.SetActive(alive);
            if(!alive) return;
            shot.Glyph.gameObject.SetActive(elapsed<.26f);
            Vector3 direction=shot.To-shot.From;
            if(direction.sqrMagnitude<.0001f) direction=shot.Normal*.03f;
            Quaternion face=Quaternion.LookRotation(shot.Normal);
            shot.Glyph.SetPositionAndRotation(shot.From+shot.Normal*.025f,face);
            shot.Glyph.localScale=Vector3.one*(.83f+elapsed*2.90f);
            Alpha(shot.RuneGlow,spellColors[type],Mathf.Clamp01(1-elapsed/.265f));
            shot.Shot.gameObject.SetActive(!barrier&&elapsed<transit+(lightning?.09f:.027f));
            if(shot.Shot.gameObject.activeSelf)
            {
                float t=Mathf.Clamp01(elapsed/transit), arc=spellArcs[type];
                Vector3 position=lightning?shot.From:Vector3.Lerp(shot.From,shot.To,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*arc);
                Vector3 d=direction+Vector3.up*Mathf.Cos(t*Mathf.PI)*arc*Mathf.PI;
                Quaternion rotation=Quaternion.FromToRotation(Vector3.up,d);
                if(!lightning) rotation*=Quaternion.Euler(0,elapsed*440,0);
                shot.Shot.SetPositionAndRotation(position,rotation);
                switch((RuneSpell)type)
                {
                    case RuneSpell.Lightning: shot.Shot.localScale=new Vector3(1,direction.magnitude,1);break;
                    case RuneSpell.Ice:shot.Shot.localScale=new Vector3(.48f,.93f,.48f);break;
                    case RuneSpell.Rock:shot.Shot.localScale=Vector3.one*.185f;break;
                    case RuneSpell.Wind:shot.Shot.localScale=new Vector3(1.13f,1.43f,1);break;
                    default:shot.Shot.localScale=new Vector3(.113f,.177f,.113f);break;
                }
            }
            float impact=elapsed-transit;
            shot.Hit.gameObject.SetActive(!barrier&&impact>=0);
            if(!shot.Hit.gameObject.activeSelf) return;
            shot.Hit.SetPositionAndRotation(shot.To,face);
            float progress=Mathf.Clamp01(impact/.35f);
            Alpha(shot.HitGlow,spellColors[type],(1-progress)*.86f);
            shot.Hit.GetChild(shot.Debris.Length).localScale=Vector3.one*(.41f+progress*2.9f);
            for(int i=0;i<shot.Debris.Length;i++)
            {
                float a=i*Mathf.PI*2/shot.Debris.Length;
                shot.Debris[i].localPosition=new Vector3(Mathf.Cos(a)*progress*.57f,Mathf.Sin(a)*progress*.61f-progress*progress*.14f,-.27f*progress);
                shot.Debris[i].localScale=Vector3.one*(.14f*(1-progress)+.034f);
                shot.Debris[i].localRotation=Quaternion.Euler(16+i*23,progress*450,i*71);
            }
        }

        private void BuildEnvironment()
        {
            var art = Group(transform,"Starfall Courtyard");
            var panorama=Resources.Load<Texture2D>("Runeveil/TwilightValley");
            if (!panorama)
            {
                panorama=Keep(new Texture2D(4,64,TextureFormat.RGBA32,false));
                var pixels=new Color[4*64];
                for (int j=0;j<64;j++)
                {
                    Color c=Color.Lerp(ColorHex(0xea91a3),ColorHex(0x724892),Mathf.Clamp01(Mathf.Abs(j/63f-.46f)*2.3f));
                    for(int i=0;i<4;i++) pixels[i+j*4]=c;
                }
                panorama.SetPixels(pixels); panorama.Apply(false,true);
            }
            var sky=Paint(0xffffff,true,1,panorama);
            sky.SetFloat("_Cull",(int)CullMode.Front);
            Draw(art,Orb(54,25,0),sky,Vector3.zero,Vector3.one*66,"Twilight panorama",Quaternion.Euler(0,180,0));
            Draw(art,Crescent(),ivory,new Vector3(-12.9f,8.97f,28f),Vector3.one*1.17f,"Amber crescent",Quaternion.Euler(0,0,-17));
            Sun(art,"Amber sunset light",0xffcda1,2.03f,new Vector3(43,25,0));
            Sun(art,"Lavender fill",0xcaa7ff,.70f,new Vector3(29,208,0));
            Draw(art,planeMesh,Paint(0x716448),new Vector3(0,-.117f,24),new Vector3(62,68,1),"Mossy soil",Quaternion.Euler(90,0,0));

            var batch=new Batch();
            for(int row=0;row<18;row++)
            for(int col=-7;col<=7;col++)
            {
                float x=col*.92f+row%2*.42f+Range(-.036f,.036f);
                float z=-1.96f+row*.879f;
                if(Mathf.Abs(x)>5.4f&&rng.NextDouble()<.2) continue;
                batch.Add(beveledBlock,Pick(stones),new Vector3(x,-.068f+Range(-.008f,.008f),z),
                    new Vector3(Range(.389f,.446f),.076f,Range(.366f,.427f)),Quaternion.Euler(Range(-2,2),Range(-5,5),0));
                if(rng.NextDouble()<.25)
                    batch.Add(ballMesh,Pick(moss),new Vector3(x+.41f,.003f,z+Range(-.23f,.23f)),new Vector3(Range(.064f,.143f),.022f,.136f));
            }
            Arch(batch,-4.20f,4.33f,1.24f,0b001111000);
            Arch(batch,4.21f,4.62f,1.22f,0);
            Arch(batch,-4.31f,8.64f,1.04f,0b000100000);
            Arch(batch,4.95f,9.55f,1.07f,0b100000000);
            Arch(batch,-6.4f,13.6f,1.17f,0b000000011);
            Arch(batch,6.92f,14.8f,1.1f,0);

            for(int i=0;i<62;i++)
            {
                int side=i%2==0?-1:1;
                float x=side*Range(2.69f,7.24f),z=Range(.25f,14.2f);
                float size=Range(.13f,z<5.9f?.76f:.43f);
                batch.Add(rockMesh,Pick(stones),new Vector3(x,size*.30f,z),new Vector3(size,size*.69f,size*Range(.64f,1f)),
                    Quaternion.Euler(Range(-20,20),Range(0,360),Range(-24,24)));
                if(i%3!=0) ScatterMoss(batch,new Vector3(x,size*.88f,z-.105f),size*.49f,5);
                if(i%2==0)
                for (int blade=-1;blade<=1;blade++)
                {
                    float h=Range(.064f,.14f);
                    batch.Add(ballMesh,Pick(moss),new Vector3(x+size*.82f+blade*.039f,h*.56f,z-.18f),new Vector3(.027f,h,.026f),
                        Quaternion.Euler(0,Range(0,85),blade*-26));
                }
            }

            var evergreen=Lathe(10,new Vector2(0,-.34f),new Vector2(.83f,-.34f),new Vector2(1,-.15f),
                new Vector2(.75f,.057f),new Vector2(.41f,.40f),new Vector2(0,.99f));
            var trunk=Lathe(8,new Vector2(1,-1),new Vector2(.51f,1));
            var bark=Paint(0x63505a);
            var fir=new[]{Paint(0x53635d),Paint(0x5c6654),Paint(0x62576b)};
            for(int i=0;i<30;i++)
            {
                int side=i%2==0?-1:1;
                float x=side*Range(3.52f,11.1f),z=Range(6.86f,23.8f),h=Range(2.13f,4.54f);
                var color=Pick(fir);
                batch.Add(trunk,bark,new Vector3(x,h*.219f-.13f,z),new Vector3(h*.063f,h*.27f,h*.067f));
                for(int tier=0;tier<4;tier++)
                {
                    float width=(.224f-tier*.039f)*h;
                    batch.Add(evergreen,color,new Vector3(x+side*tier*.05f,h*(.37f+tier*.148f),z),new Vector3(width,h*.23f,width),
                        Quaternion.Euler(0,Range(0,360),side*-6));
                }
            }
            batch.Place(this,art,"Paving, arches and trees");
            var metal=Paint(0x643939);
            var rim=Paint(0xab794c);
            var glass=Lathe(6,new Vector2(.66f,-1),new Vector2(1,1));
            Lantern(art,glass,metal,rim,new Vector3(-2.84f,1.65f,4.11f),1,-1);
            Lantern(art,glass,metal,rim,new Vector3(2.80f,1.7f,4.30f),1,1);
            Lantern(art,glass,metal,rim,new Vector3(-3.17f,1.32f,8.10f),.77f,-1);
            Lantern(art,glass,metal,rim,new Vector3(3.55f,1.31f,8.86f),.80f,1);
        }

        private void Arch(Batch batch,float x,float z,float size,int missing)
        {
            float radius=size*1.033f;
            float spring=size*1.58f;
            for(int side=-1;side<=1;side+=2)
            {
                float pier=x+side*radius;
                batch.Add(beveledBlock,Pick(stones),new Vector3(pier,.128f*size,z),new Vector3(.44f,.16f,.41f)*size);
                for(int level=0;level<4;level++)
                    batch.Add(beveledBlock,Pick(stones),new Vector3(pier+Range(-.027f,.027f),(.429f+level*.313f)*size,z),
                        new Vector3(.297f,.162f,.31f)*size,Quaternion.Euler(0,Range(-4,4),Range(-3,3)));
                ScatterMoss(batch,new Vector3(pier,.30f*size,z-.298f*size),.24f*size,5);
            }
            for(int i=0;i<9;i++)
            {
                if((missing&(1<<i))!=0) continue;
                float a=(i+.5f)*Mathf.PI/9f;
                Vector3 outward=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                Vector3 at=new Vector3(x,spring,z)+outward*radius;
                batch.Add(beveledBlock,Pick(stones),at,new Vector3(.192f,.286f,.322f)*size,
                    Quaternion.Euler(0,Range(-3,3),a*Mathf.Rad2Deg+90));
                if(i%2!=0) continue;
                Vector3 crown=at+outward*(.252f*size)+Vector3.back*(.175f*size);
                ScatterMoss(batch,crown,.195f*size,7);
                for(int leaf=0;leaf<4;leaf++)
                    batch.Add(ballMesh,Pick(moss),crown+new Vector3(Range(-.097f,.097f),-.08f*leaf,-.132f*size),
                        new Vector3(.067f,Range(.074f,.126f),.041f)*size,Quaternion.Euler(0,0,Range(-24,24)));
            }
        }

        private void ScatterMoss(Batch batch,Vector3 at,float radius,int count)
        {
            for(int i=0;i<count;i++)
            {
                float a=Range(0,Mathf.PI*2), r=Mathf.Sqrt(Range(0,1))*radius,s=radius*Range(.29f,.58f);
                batch.Add(ballMesh,Pick(moss),at+new Vector3(Mathf.Cos(a)*r,Range(-.025f,0),Mathf.Sin(a)*r*.64f),
                    new Vector3(s,s*.29f,s*.84f),Quaternion.Euler(0,Range(0,360),0));
            }
        }

        private void Lantern(Transform parent,Mesh glass,Material metal,Material rim,Vector3 at,float size,int side)
        {
            var lamp=Group(parent,"Amber lantern",at);
            lamp.localScale=Vector3.one*size;
            Draw(lamp,glass,amber,Vector3.zero,new Vector3(.126f,.156f,.126f),"Golden glass");
            Draw(lamp,glass,metal,new Vector3(0,.174f,0),new Vector3(.157f,.040f,.157f),"Cap",Quaternion.Euler(0,0,180));
            Draw(lamp,glass,metal,new Vector3(0,-.173f,0),new Vector3(.128f,.036f,.128f),"Base");
            var frame=new Batch();
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI/3;
                frame.Add(beveledBlock,rim,new Vector3(Mathf.Sin(a)*.113f,0,Mathf.Cos(a)*.113f),new Vector3(.012f,.169f,.012f));
            }
            frame.Place(this,lamp,"Bronze cage");
            var hook=new Vector3[24];
            for(int i=0;i<hook.Length;i++)
            {
                float t=i/(float)(hook.Length-1),a=t*Mathf.PI*1.7f-.41f,r=Mathf.Lerp(.221f,.049f,t);
                hook[i]=new Vector3(side*(.108f+Mathf.Cos(a)*r),.314f+Mathf.Sin(a)*r,.044f);
            }
            Draw(lamp,Tube("Curled hook",hook,.019f),metal,Vector3.zero,Vector3.one,"Iron hook");
            Draw(parent,planeMesh,lampPool,new Vector3(at.x,.023f,at.z-.12f),new Vector3(2.22f,1.6f,1)*size,"Warm light pool",Quaternion.Euler(90,0,0));
        }

        private void Sun(Transform parent,string name,uint color,float intensity,Vector3 pose)
        {
            var node=Group(parent,name);
            node.localRotation=Quaternion.Euler(pose);
            var light=node.gameObject.AddComponent<Light>();
            light.type=LightType.Directional;
            light.color=ColorHex(color);
            light.intensity=intensity;
            light.shadows=LightShadows.None;
        }

        private Mesh Crescent()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            for(int i=0;i<=33;i++)
            {
                float y=(i/33f*2-1)*.998f;
                float outer=Mathf.Sqrt(Mathf.Max(0,1-y*y));
                float inner=.43f-Mathf.Sqrt(Mathf.Max(0,.77f-(y-.085f)*(y-.085f)));
                v.Add(new Vector3(-outer,y,0)); v.Add(new Vector3(Mathf.Clamp(inner,-outer,outer),y,0));
                uv.Add(Vector2.zero);uv.Add(Vector2.one);
                if(i==33) continue;
                int n=i*2;
                t.Add(n);t.Add(n+2);t.Add(n+1);t.Add(n+1);t.Add(n+2);t.Add(n+3);
            }
            return Geometry("Carved crescent",v,t,uv);
        }

        private void BuildCreature()
        {
            golem=Group(transform,"Amberheart | Mossbound Golem",new Vector3(0,0,5.73f));
            Draw(transform,planeMesh,contact,new Vector3(0,.024f,5.39f),new Vector3(5.25f,3.36f,1),"Contact shadow",Quaternion.Euler(90,0,0));
            foreach(float x in new[]{-.82f,.82f,1.64f})
                Draw(transform,planeMesh,contact,new Vector3(x,.026f,4.99f),new Vector3(1.69f,1.31f,1),"Stone on paving",Quaternion.Euler(90,0,0));
            var dark=Paint(0x62455f); var body=Paint(0x775271); var stone=Paint(0x966780); var toe=Paint(0x95687d);
            var recess=Paint(0x452d47,true); var groove=Paint(0x5a3654);
            var bronze=Paint(0xbd8467); var copper=Paint(0x875454);
            Body(golem,body,new Vector3(0,1.52f,.106f),new Vector3(1.12f,1.13f,.837f),Quaternion.Euler(0,-5,-7));
            Body(golem,dark,new Vector3(0,.95f,.16f),new Vector3(.86f,.548f,.69f));
            foreach(int side in new[]{-1,1})
            {
                var foot=Group(golem,"Planted stone foot",new Vector3(side*.72f,0,-.13f));
                foot.localRotation=Quaternion.Euler(0,-side*13,0);
                Body(foot,body,new Vector3(side*.067f,.495f,.04f),new Vector3(.48f,.55f,.565f),Quaternion.Euler(0,0,-side*18));
                Body(foot,stone,new Vector3(side*.11f,.197f,-.275f),new Vector3(.54f,.30f,.589f));
                boxHits.Add(Draw(foot,beveledBlock,toe,new Vector3(side*.025f,.131f,-.555f),new Vector3(.35f,.16f,.27f),"Carved toe",Quaternion.Euler(-6,0,3)));
                MossLedge(foot,new Vector3(side*.105f,.415f,-.336f),.55f,8);
            }
            Body(golem,body,new Vector3(0,2.556f,.081f),new Vector3(.739f,.671f,.649f),Quaternion.Euler(0,13,9));
            Draw(golem,ballMesh,groove,new Vector3(0,2.536f,-.468f),new Vector3(.572f,.464f,.163f),"Face rim");
            Draw(golem,ballMesh,recess,new Vector3(0,2.56f,-.529f),new Vector3(.491f,.391f,.139f),"Eggplant face inset");
            var iris=Paint(0xffd6a3,true);
            var pupil=Paint(0x372638,true);
            var hornColors=new[]{Paint(0xb47c8a),Paint(0x98697f),stone};
            foreach(int side in new[]{-1,1})
            {
                Draw(golem,ballMesh,iris,new Vector3(side*.208f,2.575f,-.669f),new Vector3(.16f,.147f,.045f),"Amber eye",Quaternion.Euler(0,-side*10,side*23));
                Draw(golem,ballMesh,pupil,new Vector3(side*.151f,2.555f,-.717f),new Vector3(.049f,.072f,.015f),"Slate pupil");
                Draw(golem,ballMesh,ivory,new Vector3(side*.15f-.014f,2.583f,-.735f),new Vector3(.014f,.016f,.008f),"Eye highlight");
                for(int part=0;part<4;part++)
                {
                    float start=part*.25f+.005f,end=(part+1)*.25f-.007f;
                    var curve=new Vector3[6];
                    for(int i=0;i<curve.Length;i++)
                    {
                        float t=Mathf.Lerp(start,end,i/(float)(curve.Length-1));
                        curve[i]=new Vector3(side*(.48f+Mathf.Sin(t*Mathf.PI)*.321f-.04f*t),2.898f+.765f*t,-.103f-.14f*t);
                    }
                    Draw(golem,Tube("Tapered horn",curve,t=>.022f+.255f*Mathf.Pow(1-Mathf.Lerp(start,end,t),.66f)),
                        hornColors[part%3],Vector3.zero,Vector3.one,"Segmented stone horn");
                }
            }
            MossLedge(golem,new Vector3(0,3.084f,-.31f),1.10f,14);
            MossLedge(golem,new Vector3(-.556f,2.765f,-.342f),.41f,6);
            MossLedge(golem,new Vector3(.596f,2.745f,-.366f),.39f,6);
            var chest=Breastplate();
            Draw(golem,chest,copper,new Vector3(0,1.838f,-.56f),new Vector3(1.17f,1.11f,1),"Stone breastplate rim");
            Draw(golem,chest,bronze,new Vector3(0,1.842f,-.646f),new Vector3(1.02f,.903f,.7f),"Honey copper chest");
            foreach(int side in new[]{-1,1})
            {
                var curl=new Vector3[31];
                for(int i=0;i<curl.Length;i++)
                {
                    float t=i/(float)(curl.Length-1),a=-.49f+t*6.82f,r=.191f*(1-t)+.021f;
                    curl[i]=new Vector3(side*(.518f+Mathf.Cos(a)*r),1.844f+Mathf.Sin(a)*r,-.729f+Mathf.Cos(a)*r*.067f);
                }
                Draw(golem,Tube("Chest scroll",curl,.044f),copper,Vector3.zero,Vector3.one,"Copper vine scroll");
                Draw(golem,Tube("Stone crease",new[]{new Vector3(side*.864f,1.65f,-.512f),new Vector3(side*.908f,1.533f,-.512f),new Vector3(side*.838f,1.465f,-.551f)},.011f),
                    groove,Vector3.zero,Vector3.one,"Incised stone crease");
            }
            Draw(golem,Tube("Amber crystal seat",Arc(.303f,.336f,0,360,37),.034f,true),copper,new Vector3(0,1.91f,-.705f),Vector3.one,"Crystal seat");
            // One unlit side facets the amber; the honey-colored side catches the dusk key.
            corePaint=Paint(0xffa833,true);
            core=Draw(golem,gemMesh,Paint(0xef7b18),new Vector3(0,1.913f,-.77f),new Vector3(1,1,.92f),"Amber weak point",Quaternion.Euler(-4,17,4));
            Draw(core,gemMesh,corePaint,new Vector3(-.051f,.031f,-.10f),new Vector3(.72f,.87f,.81f),"Bright crystal facets",Quaternion.Euler(0,-15,0));
            brackets=Group(golem,"Core targeting corners",new Vector3(0,1.913f,-1.019f));
            var outline=new Batch();
            foreach(int x in new[]{-1,1})
            foreach(int y in new[]{-1,1})
            {
                Vector3[] path={new Vector3(.289f*x,.402f*y,0),new Vector3(.373f*x,.402f*y,0),new Vector3(.399f*x,.377f*y,0),new Vector3(.40f*x,.306f*y,0)};
                outline.Add(Tube("Bracket",path,.020f),ivory,Vector3.zero,Vector3.one);
            }
            outline.Place(this,brackets,"Amber brackets");
            punchArcs=Group(golem,"Two warning crescents");
            punchPaint=Paint(0xffae56,true,.78f);
            for(int i=0;i<2;i++)
                Draw(punchArcs,Tube("Windup stroke",Arc(.71f+i*.133f,.883f+i*.127f,77,246,28),.018f-i*.004f),
                    punchPaint,Vector3.zero,Vector3.one,"Fist telegraph");
            raised=MakeArm(-1,body,stone,dark,toe);
            planted=MakeArm(1,body,stone,dark,toe);
        }

        private Arm MakeArm(int side,Material body,Material stone,Material dark,Material toe)
        {
            var arm=new Arm { Shoulder=new Vector3(side*1.02f,2.20f,.112f) };
            Body(golem,stone,arm.Shoulder,new Vector3(.65f,.607f,.614f),Quaternion.Euler(0,side*8,side*9));
            MossLedge(golem,arm.Shoulder+new Vector3(side*.105f,.496f,-.301f),.76f,10);
            arm.Upper=Group(golem,"Boulder upper arm");arm.Lower=Group(golem,"Boulder forearm");arm.Fist=Group(golem,"Sculpted stone fist");
            Body(arm.Upper,body,Vector3.zero,new Vector3(.495f,.618f,.515f));
            Body(arm.Lower,stone,Vector3.zero,new Vector3(.528f,.672f,.564f));
            Body(arm.Fist,dark,Vector3.zero,new Vector3(.549f,.55f,.529f));
            for(int i=-1;i<=1;i++)
                boxHits.Add(Draw(arm.Fist,beveledBlock,toe,new Vector3(i*.326f,i==0?.089f:.042f,-.326f),
                    new Vector3(.171f,i==0?.299f:.265f,.242f),"Square stone knuckle",Quaternion.Euler(-10,-i*5,i*6)));
            boxHits.Add(Draw(arm.Fist,beveledBlock,toe,new Vector3(-side*.445f,-.122f,-.298f),new Vector3(.135f,.235f,.202f),
                "Thumb",Quaternion.Euler(-11,side*12,-side*22)));
            MossLedge(arm.Fist,new Vector3(side*.045f,.469f,-.156f),.80f,10);
            return arm;
        }

        private void Body(Transform parent,Material paint,Vector3 at,Vector3 scale,Quaternion pose=default) =>
            stoneHits.Add(Draw(parent,rockMesh,paint,at,scale,"Molded purple stone",pose));

        private void MossLedge(Transform parent,Vector3 at,float width,int count)
        {
            var patch=new Batch();
            for(int i=0;i<count;i++)
            {
                float x=(i/(float)(count-1)-.5f)*width,r=width*Range(.123f,.175f);
                float edge=Mathf.Pow(x/(width*.5f),2)*width*.187f;
                patch.Add(ballMesh,Pick(moss),at+new Vector3(x,-edge-width*.049f,width*Range(.019f,.072f)),
                    new Vector3(r,r*Range(.55f,1.22f),r*1.17f),Quaternion.Euler(-19,0,Mathf.Sign(x)*Range(8,31)));
            }
            patch.Place(this,parent,"Scalloped moss mantle");
        }

        private Mesh Breastplate()
        {
            Vector2[] edge={new Vector2(0,-.657f),new Vector2(-.29f,-.429f),new Vector2(-.59f,-.217f),new Vector2(-.75f,.015f),
                new Vector2(-.683f,.22f),new Vector2(-.49f,.329f),new Vector2(-.235f,.338f),new Vector2(0,.276f),
                new Vector2(.235f,.338f),new Vector2(.49f,.329f),new Vector2(.683f,.22f),new Vector2(.75f,.015f),
                new Vector2(.59f,-.217f),new Vector2(.29f,-.429f)};
            var vertices=new List<Vector3>{new Vector3(0,0,-.088f)};
            var tri=new List<int>();var uv=new List<Vector2>{Vector2.one*.5f};
            for(int ring=0;ring<3;ring++)
            for(int i=0;i<edge.Length;i++)
            {
                Vector2 p=edge[i]*(ring==0?.878f:ring==1?1:.94f);
                vertices.Add(new Vector3(p.x,p.y,ring==0?-.088f:ring==1?-.022f:.041f));uv.Add(p+Vector2.one*.5f);
                int a=1+ring*edge.Length+i,b=1+ring*edge.Length+(i+1)%edge.Length;
                if(ring==0) {tri.Add(0);tri.Add(a);tri.Add(b);}
                if(ring>=2) continue;
                tri.Add(a);tri.Add(a+edge.Length);tri.Add(b);tri.Add(b);tri.Add(a+edge.Length);tri.Add(b+edge.Length);
            }
            return Geometry("Beveled heart plaque",vertices,tri,uv);
        }

        private static Vector3[] Arc(float width,float height,float start,float end,int steps)
        {
            Vector3[] points=new Vector3[steps];
            for(int i=0;i<points.Length;i++)
            {
                float a=Mathf.Lerp(start,end,i/(float)(points.Length-1))*Mathf.Deg2Rad;
                points[i]=new Vector3(width*Mathf.Cos(a),height*Mathf.Sin(a),0);
            }
            return points;
        }

        private Mesh Tube(string name,Vector3[] path,float width,bool loop=false) => Tube(name,path,_=>width,loop);

        private Mesh Tube(string name,Vector3[] path,Func<float,float> width,bool loop=false)
        {
            var v=new List<Vector3>(); var t=new List<int>(); var uv=new List<Vector2>();
            const int sides=7;
            for(int i=0;i<path.Length;i++)
            {
                var before=path[i==0?(loop?path.Length-1:0):i-1];
                var after=path[i==path.Length-1?(loop?0:i):i+1];
                Vector3 direction=(after-before).normalized;
                Vector3 right=Vector3.Cross(direction,Mathf.Abs(direction.z)>.94f?Vector3.up:Vector3.forward).normalized;
                Vector3 up=Vector3.Cross(direction,right);
                for(int k=0;k<sides;k++)
                {
                    float a=k*Mathf.PI*2/sides;
                    v.Add(path[i]+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*width(i/(float)(path.Length-1)));
                    uv.Add(new Vector2(k/(float)sides,i/(float)(path.Length-1)));
                    if(i==path.Length-1&&!loop) continue;
                    int x=i*sides+k,y=i*sides+(k+1)%sides;
                    int z=(i+1)%path.Length*sides+k,w=(i+1)%path.Length*sides+(k+1)%sides;
                    t.Add(x);t.Add(y);t.Add(z);t.Add(y);t.Add(w);t.Add(z);
                }
            }
            return Geometry(name,v,t,uv);
        }

        private void OnDestroy()
        {
            foreach(var obj in owned) { if(obj) Destroy(obj); }
        }
    }
}
