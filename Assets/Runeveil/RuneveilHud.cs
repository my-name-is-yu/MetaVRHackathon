using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Runeveil
{
    public sealed class RuneveilHud : MonoBehaviour
    {
        private const float PanelDistance = 1.65f;
        private const float SpatialScale = 0.00082f;
        private const int BossBarWidth = 416;

        private static readonly Color32 Ink = new Color32(36, 26, 46, 244);
        private static readonly Color32 Tile = new Color32(56, 41, 64, 255);
        private static readonly Color32 Edge = new Color32(137, 104, 112, 255);
        private static readonly Color32 Gold = new Color32(253, 200, 124, 255);
        private static readonly Color32 Paper = new Color32(255, 235, 207, 255);
        private static readonly Color32 Muted = new Color32(211, 191, 210, 255);
        private static readonly Color32 Mint = new Color32(159, 242, 206, 255);

        private Camera view;
        private RectTransform canvasRect;
        private Font font;
        private Vector3 spatialOrigin;
        private Quaternion spatialRotation;
        private float desktopAspect;
        private float desktopFieldOfView;
        private bool spatial;

        private RectTransform boss;
        private RectTransform guide;
        private RectTransform vitality;
        private RectTransform targeting;
        private RectTransform action;
        private RectTransform notice;
        private Image actionFill;
        private Text actionText;
        private Text bossHpText;
        private RectTransform bossFill;
        private Text guideControls;
        private Text wardText;
        private readonly Text[] hearts = new Text[3];
        private Text focusText;
        private Text spellText;
        private Text noticeText;
        private bool actionFocused;
        private string controlHint;
        private string currentAction;
        private int currentBossHp = -1;

        private RectTransform handCard;
        private Vector3? handPosition;
        private bool inkBeingDrawn;
        private LineRenderer stroke;
        private Material strokeMaterial;

        private const float PinchBarWidth = 180f;
        private const float PinchBarLeft = -125f;
        private RectTransform pinchMeter;
        private readonly PinchRow[] pinchRows = new PinchRow[2];

        private struct PinchRow
        {
            public Text Name;
            public RectTransform Fill;
            public Image FillImage;
            public RectTransform StartMark;
            public RectTransform ReleaseMark;
            public Text Detail;
        }

        public void Initialize(Camera camera, Transform spatialReference)
        {
            view = camera;
            spatialOrigin = spatialReference.position;
            Vector3 facing = Vector3.ProjectOnPlane(spatialReference.forward, Vector3.up);
            spatialRotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.001f ? facing : Vector3.forward);
            if (canvasRect != null)
            {
                canvasRect.GetComponent<Canvas>().worldCamera = view;
                handCard.GetComponent<Canvas>().worldCamera = view;
                SetLayout(spatial);
                return;
            }

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasRect = NewCanvas("Duel HUD");
            CreateBossPanel();
            CreateGuide();
            CreateVitalityPanel();
            CreateTargetingPanel();
            CreateAction();
            notice = Panel("Duel event", canvasRect, 570, 80);
            noticeText = Label("Message", notice, "", 23, Paper, 542, 70, 0, 0);
            notice.gameObject.SetActive(false);

            handCard = NewCanvas("Casting hand");
            RectTransform card = Panel("Casting feedback", handCard, 186, 42);
            Label("Feedback", card, "Drawing rune", 23, Mint, 166, 38, 0, 0);
            handCard.gameObject.SetActive(false);

            var strokeObject = new GameObject("Rune ink");
            strokeObject.transform.SetParent(transform, false);
            stroke = strokeObject.AddComponent<LineRenderer>();
            stroke.useWorldSpace = true;
            stroke.widthMultiplier = 0.0064f;
            stroke.numCapVertices = 4;
            stroke.numCornerVertices = 3;
            stroke.shadowCastingMode = ShadowCastingMode.Off;
            stroke.receiveShadows = false;
            Shader inkShader = Shader.Find("Sprites/Default");
            if (inkShader != null)
            {
                strokeMaterial = new Material(inkShader);
                stroke.sharedMaterial = strokeMaterial;
            }

            stroke.enabled = false;
            SetLayout(false);
        }

        public void Render(int bossHp, int playerHearts, bool wardActive, string focusLabel,
            string spellLabel, string status, bool showGuide, string actionLabel, bool xrInput)
        {
            if (xrInput != spatial)
                SetLayout(xrInput);

            bossHp = Mathf.Clamp(bossHp, 0, RuneveilDuel.MaxBossHealth);
            if (currentBossHp != bossHp)
            {
                currentBossHp = bossHp;
                bossHpText.text = $"{bossHp} / {RuneveilDuel.MaxBossHealth}";
                bossFill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    BossBarWidth * (bossHp / (float)RuneveilDuel.MaxBossHealth));
            }

            for (int i = 0; i < hearts.Length; i++)
                hearts[i].color = i < playerHearts ? new Color32(255, 166, 164, 255) : new Color32(110, 80, 104, 255);

            wardText.text = wardActive ? "WARD READY" : "WARD EMPTY";
            wardText.color = wardActive ? Mint : Muted;
            focusText.text = string.IsNullOrEmpty(focusLabel) ? "—" : focusLabel;
            spellText.text = string.IsNullOrEmpty(spellLabel) ? "—" : spellLabel;
            guide.gameObject.SetActive(showGuide);
            if (currentAction != actionLabel)
            {
                currentAction = actionLabel;
                action.gameObject.SetActive(!string.IsNullOrEmpty(actionLabel));
                actionText.text = string.IsNullOrEmpty(actionLabel) ? "" : $"{actionLabel}  ›";
            }
            notice.gameObject.SetActive(!string.IsNullOrEmpty(status));
            noticeText.text = status ?? "";
        }

        public void SetControlHint(string hint)
        {
            if (controlHint == hint)
                return;

            controlHint = hint;
            if (guideControls != null)
                RefreshControls();
        }

        public bool ActionHit(Ray ray)
        {
            if (action == null || !action.gameObject.activeInHierarchy)
                return false;

            if (!spatial)
                PositionDesktopHud();

            var plane = new Plane(action.forward, action.position);
            if (!plane.Raycast(ray, out float distance))
                return false;

            Vector3 hit = action.InverseTransformPoint(ray.GetPoint(distance));
            Rect target = action.rect;
            Vector2 hitPadding = new Vector2(10, spatial ? 22 : 10);
            target.min -= hitPadding;
            target.max += hitPadding;
            return target.Contains(new Vector2(hit.x, hit.y));
        }

        public void SetActionFocus(Ray? pointingRay)
        {
            bool focused = pointingRay.HasValue && ActionHit(pointingRay.Value);
            if (focused == actionFocused)
                return;

            actionFocused = focused;
            actionFill.color = focused ? new Color32(255, 229, 177, 255) : Gold;
        }

        public void ShowStroke(IReadOnlyList<Vector3> points, Color color, float opacity = 1f)
        {
            if (stroke == null)
                return;

            int count = points?.Count ?? 0;
            stroke.enabled = count > 1 && opacity > 0f;
            inkBeingDrawn = stroke.enabled && opacity >= 1f;
            if (!stroke.enabled)
            {
                stroke.positionCount = 0;
                return;
            }

            color.a = Mathf.Clamp01(opacity);
            stroke.startColor = color;
            stroke.endColor = color;
            stroke.positionCount = count;
            for (int i = 0; i < count; i++)
                stroke.SetPosition(i, points[i]);
        }

        /// <summary>Live pinch meter for tuning on the headset. Shown only in the spatial layout.</summary>
        public void SetPinchMeter(bool visible, RuneveilPinchDetector left, RuneveilPinchDetector right,
            RuneveilPinchSettings settings)
        {
            if (canvasRect == null)
                return;

            if (!visible || !spatial)
            {
                if (pinchMeter != null && pinchMeter.gameObject.activeSelf)
                    pinchMeter.gameObject.SetActive(false);
                return;
            }

            if (pinchMeter == null)
                CreatePinchMeter();
            if (!pinchMeter.gameObject.activeSelf)
                pinchMeter.gameObject.SetActive(true);
            UpdatePinchRow(pinchRows[0], "L", left, settings);
            UpdatePinchRow(pinchRows[1], "R", right, settings);
        }

        private void CreatePinchMeter()
        {
            pinchMeter = Panel("Pinch meter", canvasRect, 358, 101);
            pinchMeter.anchoredPosition = new Vector2(422, -405);
            pinchMeter.localScale = Vector3.one * 0.9f;
            for (int i = 0; i < pinchRows.Length; i++)
            {
                float y = i == 0 ? 21 : -21;
                var row = new PinchRow();
                row.Name = Label("Hand", pinchMeter, "", 19, Muted, 40, 34, -155, y, TextAnchor.MiddleCenter, true);
                ImageBox("Track", pinchMeter, PinchBarWidth, 14, PinchBarLeft + PinchBarWidth * 0.5f, y, Tile);
                row.FillImage = ImageBox("Strength", pinchMeter, PinchBarWidth, 14, PinchBarLeft, y, Muted);
                row.Fill = row.FillImage.rectTransform;
                row.Fill.pivot = new Vector2(0, 0.5f);
                row.Fill.anchoredPosition = new Vector2(PinchBarLeft, y);
                row.ReleaseMark = ImageBox("Release threshold", pinchMeter, 3, 24, 0, y, Edge).rectTransform;
                row.StartMark = ImageBox("Start threshold", pinchMeter, 3, 24, 0, y, Gold).rectTransform;
                row.Detail = Label("Distance", pinchMeter, "", 18, Paper, 110, 34, 118, y, TextAnchor.MiddleRight);
                pinchRows[i] = row;
            }
        }

        private static void UpdatePinchRow(PinchRow row, string hand, RuneveilPinchDetector pinch,
            RuneveilPinchSettings settings)
        {
            float strength = pinch != null ? Mathf.Clamp01(pinch.Strength) : 0f;
            row.Fill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, PinchBarWidth * strength);
            float y = row.Fill.anchoredPosition.y;
            row.StartMark.anchoredPosition = new Vector2(PinchBarLeft + PinchBarWidth * settings.startStrength, y);
            row.ReleaseMark.anchoredPosition = new Vector2(PinchBarLeft + PinchBarWidth * settings.releaseStrength, y);
            row.StartMark.gameObject.SetActive(settings.useStrength);
            row.ReleaseMark.gameObject.SetActive(settings.useStrength);

            bool tracked = pinch != null && pinch.Tracked;
            Color state = !tracked ? Edge : pinch.InGrace ? Gold : pinch.Pinching ? Mint : Muted;
            row.Name.text = hand;
            row.Name.color = state;
            row.FillImage.color = state;
            if (!tracked)
            {
                row.Detail.text = "no hand";
                return;
            }
            string distance = float.IsInfinity(pinch.Distance) ? "--" : $"{pinch.Distance * 100f:0.0}cm";
            row.Detail.text = pinch.SystemPinch ? distance + " •" : distance;
        }

        public void SetHand(Vector3? worldPosition)
        {
            handPosition = worldPosition;
        }

        private void LateUpdate()
        {
            if (canvasRect == null || view == null)
                return;

            if (!spatial)
                PositionDesktopHud();

            Vector3 local = handPosition.HasValue
                ? view.transform.InverseTransformPoint(handPosition.Value)
                : Vector3.zero;
            bool showHand = spatial && local.z > 0.16f && inkBeingDrawn;
            handCard.gameObject.SetActive(showHand);
            if (!showHand)
                return;

            local.y -= 0.055f;
            handCard.SetPositionAndRotation(view.transform.TransformPoint(local), view.transform.rotation);
            handCard.localScale = Vector3.one * Mathf.Clamp(local.z * 0.0017f, 0.00056f, 0.00095f);
        }

        private void OnDestroy()
        {
            if (strokeMaterial != null)
                Destroy(strokeMaterial);
        }

        private void SetLayout(bool xrInput)
        {
            spatial = xrInput;
            RefreshControls();
            Vector3 panelScale = Vector3.one * (spatial ? 0.9f : 1f);
            foreach (RectTransform panel in new[] { boss, guide, vitality, targeting, action, notice })
                panel.localScale = panelScale;

            if (!spatial)
            {
                desktopAspect = 0f;
                PositionDesktopHud();
                return;
            }

            canvasRect.SetPositionAndRotation(spatialOrigin + spatialRotation * (Vector3.forward * PanelDistance),
                spatialRotation);
            canvasRect.localScale = Vector3.one * SpatialScale;
            // Keep panels inside the central binocular view of Meta VR Glasses.
            boss.anchoredPosition = new Vector2(387, 490);
            guide.anchoredPosition = new Vector2(-409, -21);
            vitality.anchoredPosition = new Vector2(-422, -518);
            targeting.anchoredPosition = new Vector2(422, -518);
            action.anchoredPosition = new Vector2(-409, -254);
            notice.anchoredPosition = new Vector2(0, -390);
        }

        private void PositionDesktopHud()
        {
            Transform eye = view.transform;
            Vector3 position = eye.position + eye.forward * PanelDistance;
            if (canvasRect.position != position || canvasRect.rotation != eye.rotation)
                canvasRect.SetPositionAndRotation(position, eye.rotation);

            if (Mathf.Approximately(desktopAspect, view.aspect) &&
                Mathf.Approximately(desktopFieldOfView, view.fieldOfView))
                return;

            desktopAspect = view.aspect;
            desktopFieldOfView = view.fieldOfView;
            float height = 2f * Mathf.Tan(desktopFieldOfView * 0.5f * Mathf.Deg2Rad) * PanelDistance;
            float halfWidth = 475f * desktopAspect;
            canvasRect.localScale = Vector3.one * (height / 950f);
            float guideX = -Mathf.Max(230, halfWidth - 245);
            float statusX = Mathf.Max(203, halfWidth - 203);
            boss.anchoredPosition = new Vector2(0, 401);
            guide.anchoredPosition = new Vector2(guideX, 10);
            vitality.anchoredPosition = new Vector2(-statusX, -385);
            targeting.anchoredPosition = new Vector2(statusX, -385);
            action.anchoredPosition = new Vector2(guideX, -241);
            notice.anchoredPosition = new Vector2(0, desktopAspect >= 1.6f ? -385 : -280);
        }

        private void RefreshControls()
        {
            guideControls.text = string.IsNullOrEmpty(controlHint)
                ? (spatial
                    ? "Pinch to draw. Look at the target\nand release. To start, look and pinch."
                    : "Drag to draw and release to cast.\nRight-click to lock or release your aim.")
                : controlHint;
        }

        private void CreateBossPanel()
        {
            boss = Panel("Boss health", canvasRect, 468, 91);
            Label("Boss name", boss, "Corrupted Muse", 24, Paper, 290, 36, -65, 16, TextAnchor.MiddleLeft);
            bossHpText = Label("Health", boss, "", 21, Gold, 124, 36, 146, 16, TextAnchor.MiddleRight);
            ImageBox("Health track", boss, 420, 16, 0, -19, Edge);
            ImageBox("Health track interior", boss, BossBarWidth, 12, 0, -19, Tile);
            bossFill = ImageBox("Health remaining", boss, BossBarWidth, 12, -BossBarWidth * 0.5f, -19, Gold).rectTransform;
            bossFill.pivot = new Vector2(0, 0.5f);
            bossFill.anchoredPosition = new Vector2(-BossBarWidth * 0.5f, -19);
        }

        private void CreateGuide()
        {
            guide = Panel("Practice and spellbook", canvasRect, 440, 396);
            Label("Runeveil", guide, "Runeveil", 40, Paper, 274, 52, -60, 154, TextAnchor.MiddleLeft, true);
            Label("Practice", guide, "PRACTICE", 17, Gold, 144, 35, 126, 155, TextAnchor.MiddleRight, true);
            ImageBox("Title divider", guide, 392, 1, 0, 121, Edge);
            guideControls = Label("How to cast", guide, "", 20, Paper, 392, 67, 0, 81, TextAnchor.MiddleLeft);
            CreateGlyph(Spell.Fire, "Fireball", new Color32(255, 153, 125, 255), -132, 4);
            CreateGlyph(Spell.Lightning, "Lightning", new Color32(255, 213, 106, 255), 0, 4);
            CreateGlyph(Spell.Ice, "Ice spear", new Color32(163, 228, 255, 255), 132, 4);
            CreateGlyph(Spell.Rock, "Stone shot", new Color32(228, 185, 154, 255), -132, -78);
            CreateGlyph(Spell.Wind, "Wind blade", new Color32(207, 182, 255, 255), 0, -78);
            CreateGlyph(Spell.Barrier, "Ward", Mint, 132, -78);
            Label("Combat tip", guide, "Fist raised? Hold gaze on the pink core.\nA closed ring means extra damage.\nLightning interrupts. Ward blocks one hit.",
                18, Muted, 392, 65, 0, -153, TextAnchor.MiddleLeft);
        }

        private void CreateGlyph(Spell spell, string name, Color color, float x, float y)
        {
            RectTransform tile = ImageBox(name + " tile", guide, 124, 74, x, y, Tile).rectTransform;
            RectTransform path = RectAt(name + " glyph", tile, 54, 54, 0, 11);
            path.gameObject.AddComponent<CanvasRenderer>();
            var graphic = path.gameObject.AddComponent<RuneveilGlyphGraphic>();
            graphic.raycastTarget = false;
            graphic.SetGlyph(RuneRecognizer.GetGlyph(spell), color);
            Label(name, tile, name, 18, Paper, 122, 25, 0, -23);
        }

        private void CreateVitalityPanel()
        {
            vitality = Panel("Life and ward", canvasRect, 358, 101);
            Label("Life", vitality, "LIFE", 18, Muted, 84, 36, -113, 18, TextAnchor.MiddleLeft, true);
            for (int i = 0; i < hearts.Length; i++)
                hearts[i] = Label("Heart", vitality, "♥", 32, Paper, 38, 40, -32 + i * 40, 18);
            wardText = Label("Ward", vitality, "", 19, Mint, 292, 31, -10, -22, TextAnchor.MiddleLeft, true);
        }

        private void CreateTargetingPanel()
        {
            targeting = Panel("Focus and spell", canvasRect, 358, 101);
            Label("Focus caption", targeting, "FOCUS", 17, Muted, 104, 35, -105, 18, TextAnchor.MiddleLeft, true);
            Label("Last cast caption", targeting, "LAST CAST", 17, Muted, 110, 35, -102, -20, TextAnchor.MiddleLeft, true);
            focusText = Label("Focus name", targeting, "", 22, Mint, 212, 35, 55, 18, TextAnchor.MiddleLeft);
            spellText = Label("Last cast", targeting, "", 22, Paper, 212, 35, 55, -20, TextAnchor.MiddleLeft);
        }

        private void CreateAction()
        {
            action = RectAt("Start or rematch", canvasRect, 440, 77, 0, 0);
            actionFill = ImageBox("Button face", action, 440, 77, 0, 0, Gold);
            ImageBox("Button bottom edge", action, 440, 4, 0, -36, new Color32(203, 142, 88, 255));
            actionText = Label("Button label", action, "", 27, Ink, 406, 60, 0, 2, TextAnchor.MiddleCenter, true);
            action.gameObject.SetActive(false);
        }

        private RectTransform NewCanvas(string name)
        {
            RectTransform rect = RectAt(name, transform, 2200, 1100, 0, 0);
            Canvas canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = view;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;
            return rect;
        }

        private static RectTransform Panel(string name, Transform parent, float width, float height)
        {
            RectTransform rect = RectAt(name, parent, width, height, 0, 0);
            ImageBox("Frame", rect, width, height, 0, 0, Edge);
            ImageBox("Surface", rect, width - 4, height - 4, 0, 0, Ink);
            return rect;
        }

        private static RectTransform RectAt(string name, Transform parent, float width, float height, float x, float y)
        {
            var item = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)item.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }

        private static Image ImageBox(string name, Transform parent, float width, float height, float x, float y, Color color)
        {
            var image = RectAt(name, parent, width, height, x, y).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text Label(string name, Transform parent, string value, int size, Color color, float width, float height,
            float x, float y, TextAnchor alignment = TextAnchor.MiddleCenter, bool bold = false)
        {
            var label = RectAt(name, parent, width, height, x, y).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.alignment = alignment;
            label.color = color;
            label.text = value;
            label.raycastTarget = false;
            return label;
        }
    }

    internal sealed class RuneveilGlyphGraphic : Graphic
    {
        private IReadOnlyList<Vector2> glyph;

        public void SetGlyph(IReadOnlyList<Vector2> points, Color tint)
        {
            glyph = points;
            color = tint;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (glyph == null || glyph.Count < 2)
                return;

            const float size = 38f;
            Color32 tint = color;
            for (int i = 1; i < glyph.Count; i++)
            {
                Vector2 start = glyph[i - 1] * size;
                Vector2 end = glyph[i] * size;
                Vector2 direction = (end - start).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x) * 2f;
                Vector2 overlap = direction * 0.5f;
                AddQuad(mesh, start - overlap - normal, start - overlap + normal,
                    end + overlap + normal, end + overlap - normal, tint);
            }

            Vector2 first = glyph[0] * size;
            AddQuad(mesh, first + Vector2.left * 3.6f, first + Vector2.up * 3.6f,
                first + Vector2.right * 3.6f, first + Vector2.down * 3.6f, new Color32(255, 247, 221, 255));
        }

        private static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            int first = mesh.currentVertCount;
            Vector2 uv = Vector2.one * 0.5f;
            mesh.AddVert(a, tint, uv);
            mesh.AddVert(b, tint, uv);
            mesh.AddVert(c, tint, uv);
            mesh.AddVert(d, tint, uv);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
