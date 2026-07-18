// VERTICAL SLICE - NOT FOR PRODUCTION
// Validation Question: Does a player feel Super Ricochet's "Ready, Aim,
// Fire!" fantasy within 2-3 minutes with no guidance? This is the Unity
// view/input layer only - all scored simulation lives in
// Assets/Scripts/SharedSimCore/RicochetSim.cs. Rendering here is plain
// UGUI Image rects (no Sprite/Rigidbody2D/prefab dependencies) - a
// deliberate slice-only simplification. Production should follow
// ADR-0002 Decision 3 (kinematic Rigidbody2D, visual-only) and ADR-0008
// (InputSystemUIInputModule), neither of which this slice attempts.
// Date: 2026-07-11

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QuackStudio.SharedSimCore;

namespace VerticalSlice
{
    public class RicochetSliceController : MonoBehaviour
    {
        private const float FrameDtSeconds = 1f / 60f;
        private const float MaxCatchUpSeconds = 0.25f;
        private const ulong SliceSeed = 12345UL;

        private RicochetSim _sim;
        private float _accumulator;

        private static readonly float TotalHeightNormalized =
            RicochetSim.CellSize.ToFloatForDisplay() * RicochetSim.TotalRows;

        private Canvas _canvas;
        private RectTransform _playfieldRect;
        private RectTransform _launcherMarker;
        private RectTransform _aimLine;
        private Image _aimLineImage;

        private TextMeshProUGUI _turnText;
        private TextMeshProUGUI _ballsText;
        private TextMeshProUGUI _bossNameText;
        private TextMeshProUGUI _bossHpText;
        private RectTransform _bossHpFill;

        private GameObject _resultPanel;
        private TextMeshProUGUI _resultText;

        private readonly Dictionary<int, RectTransform> _brickViews = new Dictionary<int, RectTransform>();
        private readonly Dictionary<int, Image> _brickViewImages = new Dictionary<int, Image>();
        private readonly Dictionary<int, TextMeshProUGUI> _brickViewLabels = new Dictionary<int, TextMeshProUGUI>();
        private readonly Dictionary<int, RectTransform> _ballViews = new Dictionary<int, RectTransform>();
        private readonly HashSet<int> _liveBrickIds = new HashSet<int>();
        private readonly HashSet<int> _liveBallIds = new HashSet<int>();
        private List<int> _idsToRemoveScratch = new List<int>();

        private bool _isDragging;
        private Vector2 _dragCurrentLocal;

        private void Awake()
        {
            BuildUi();
            StartNewRun();
        }

        private void StartNewRun()
        {
            _sim = new RicochetSim();
            _sim.Initialize(SliceSeed);
            _sim.BeginAiming();
            _accumulator = 0f;
            _isDragging = false;
            _aimLineImage.enabled = false;
            ClearAllViews();
            _resultPanel.SetActive(false);
        }

        private void Update()
        {
            HandleInput();

            _accumulator += Time.deltaTime;
            if (_accumulator > MaxCatchUpSeconds)
            {
                // Spiral-of-death clamp: drop un-simulatable time after a
                // lag spike rather than stalling. Does not affect
                // determinism - the sim only ever advances in whole
                // FrameDt steps (ADR-0002 Decision 4).
                _accumulator = MaxCatchUpSeconds;
            }

            while (_accumulator >= FrameDtSeconds)
            {
                _sim.AdvanceFrame();
                _accumulator -= FrameDtSeconds;
            }

            SyncViews();

            if (_sim.State == RicochetState.Over && !_resultPanel.activeSelf)
            {
                ShowResult();
            }
        }

        // ---------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------

        private void HandleInput()
        {
            if (_sim.State != RicochetState.Aiming)
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    _aimLineImage.enabled = false;
                }
                return;
            }

            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

            if (Input.GetMouseButtonDown(0))
            {
                _isDragging = true;
                _aimLineImage.enabled = true;
            }

            if (_isDragging && Input.GetMouseButton(0))
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _playfieldRect, Input.mousePosition, cam, out _dragCurrentLocal);
                UpdateAimLine();
            }

            if (_isDragging && Input.GetMouseButtonUp(0))
            {
                _isDragging = false;
                _aimLineImage.enabled = false;

                Vector2 launcherLocal = ToLocalPos(
                    _sim.LauncherX.ToFloatForDisplay(), _sim.LauncherY.ToFloatForDisplay());
                Vector2 delta = _dragCurrentLocal - launcherLocal;

                _sim.Fire(Fix32.FromFloat(delta.x), Fix32.FromFloat(delta.y));
            }
        }

        private void UpdateAimLine()
        {
            Vector2 launcherLocal = ToLocalPos(
                _sim.LauncherX.ToFloatForDisplay(), _sim.LauncherY.ToFloatForDisplay());
            Vector2 delta = _dragCurrentLocal - launcherLocal;
            float length = delta.magnitude;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            _aimLine.anchoredPosition = launcherLocal;
            _aimLine.sizeDelta = new Vector2(length, 6f);
            _aimLine.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        // ---------------------------------------------------------------
        // Coordinate mapping (normalized sim space -> playfield local px)
        // ---------------------------------------------------------------

        private Vector2 ToLocalPos(float normX, float normYEquiv)
        {
            float w = _playfieldRect.rect.width;
            float h = _playfieldRect.rect.height;
            float x = (normX - 0.5f) * w;
            float y = (normYEquiv / TotalHeightNormalized - 0.5f) * h;
            return new Vector2(x, y);
        }

        // ---------------------------------------------------------------
        // View sync
        // ---------------------------------------------------------------

        private void SyncViews()
        {
            _bossNameText.text = _sim.BossName;
            float bossFrac = _sim.BossMaxHp > 0 ? (float)_sim.BossHp / _sim.BossMaxHp : 0f;
            _bossHpText.text = _sim.BossHp + " / " + _sim.BossMaxHp;

            Vector2 fillAnchorMax = _bossHpFill.anchorMax;
            fillAnchorMax.x = Mathf.Clamp01(bossFrac);
            _bossHpFill.anchorMax = fillAnchorMax;

            _turnText.text = "Turn " + _sim.TurnNumber;

            int ballsInFlight = 0;
            for (int i = 0; i < _sim.Balls.Count; i++)
            {
                if (!_sim.Balls[i].Retired)
                {
                    ballsInFlight++;
                }
            }

            _ballsText.text = _sim.State == RicochetState.Firing
                ? "Balls in flight: " + ballsInFlight
                : "Ready to fire";

            SyncBricks();
            SyncBalls();
            SyncLauncherMarker();
        }

        private void SyncLauncherMarker()
        {
            _launcherMarker.anchoredPosition = ToLocalPos(
                _sim.LauncherX.ToFloatForDisplay(), _sim.LauncherY.ToFloatForDisplay());
        }

        private void SyncBricks()
        {
            _liveBrickIds.Clear();

            float playfieldWidth = _playfieldRect.rect.width;
            float cellUnit = RicochetSim.CellSize.ToFloatForDisplay();
            float cellPixels = cellUnit * playfieldWidth;

            IReadOnlyList<RicochetSim.BrickState> states = _sim.BrickStates;
            for (int i = 0; i < states.Count; i++)
            {
                RicochetSim.BrickState brick = states[i];
                _liveBrickIds.Add(brick.Id);

                if (!_brickViews.TryGetValue(brick.Id, out RectTransform rt))
                {
                    var go = new GameObject("Brick_" + brick.Id, typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(_playfieldRect, false);
                    rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);

                    var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                    labelGo.transform.SetParent(rt, false);
                    var labelRt = labelGo.GetComponent<RectTransform>();
                    labelRt.anchorMin = Vector2.zero;
                    labelRt.anchorMax = Vector2.one;
                    labelRt.offsetMin = Vector2.zero;
                    labelRt.offsetMax = Vector2.zero;
                    var label = labelGo.GetComponent<TextMeshProUGUI>();
                    label.alignment = TextAlignmentOptions.Center;
                    label.color = Color.black;
                    label.fontSize = 28f;

                    _brickViews[brick.Id] = rt;
                    _brickViewImages[brick.Id] = go.GetComponent<Image>();
                    _brickViewLabels[brick.Id] = label;
                }

                float normX = brick.Column * cellUnit + cellUnit * 0.5f;
                float normYEquiv = brick.Row * cellUnit + cellUnit * 0.5f;

                rt.sizeDelta = new Vector2(cellPixels * 0.9f, cellPixels * 0.9f);
                rt.anchoredPosition = ToLocalPos(normX, normYEquiv);

                _brickViewImages[brick.Id].color = Color.Lerp(
                    new Color(1f, 0.85f, 0.4f),
                    new Color(0.75f, 0.1f, 0.1f),
                    Mathf.Clamp01(brick.Hp / 6f));

                _brickViewLabels[brick.Id].text = brick.Hp.ToString();
            }

            RemoveStaleViews(_brickViews, _liveBrickIds, _brickViewImages, _brickViewLabels);
        }

        private void SyncBalls()
        {
            _liveBallIds.Clear();

            float playfieldWidth = _playfieldRect.rect.width;
            float ballDiameterPixels = RicochetSim.BallRadius.ToFloatForDisplay() * 2f * playfieldWidth;

            IReadOnlyList<BallState> states = _sim.Balls;
            for (int i = 0; i < states.Count; i++)
            {
                BallState ball = states[i];
                if (ball.Retired)
                {
                    continue;
                }

                _liveBallIds.Add(ball.Id);

                if (!_ballViews.TryGetValue(ball.Id, out RectTransform rt))
                {
                    var go = new GameObject("Ball_" + ball.Id, typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(_playfieldRect, false);
                    rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    go.GetComponent<Image>().color = Color.white;
                    _ballViews[ball.Id] = rt;
                }

                rt.sizeDelta = new Vector2(ballDiameterPixels, ballDiameterPixels);
                rt.anchoredPosition = ToLocalPos(ball.X.ToFloatForDisplay(), ball.Y.ToFloatForDisplay());
            }

            RemoveStaleViews(_ballViews, _liveBallIds, null, null);
        }

        private void RemoveStaleViews(
            Dictionary<int, RectTransform> views,
            HashSet<int> liveIds,
            Dictionary<int, Image> imagesOrNull,
            Dictionary<int, TextMeshProUGUI> labelsOrNull)
        {
            _idsToRemoveScratch.Clear();
            foreach (var kvp in views)
            {
                if (!liveIds.Contains(kvp.Key))
                {
                    _idsToRemoveScratch.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _idsToRemoveScratch.Count; i++)
            {
                int id = _idsToRemoveScratch[i];
                Destroy(views[id].gameObject);
                views.Remove(id);
                imagesOrNull?.Remove(id);
                labelsOrNull?.Remove(id);
            }
        }

        private void ClearAllViews()
        {
            foreach (var kvp in _brickViews)
            {
                Destroy(kvp.Value.gameObject);
            }
            _brickViews.Clear();
            _brickViewImages.Clear();
            _brickViewLabels.Clear();

            foreach (var kvp in _ballViews)
            {
                Destroy(kvp.Value.gameObject);
            }
            _ballViews.Clear();
        }

        private void ShowResult()
        {
            _resultPanel.SetActive(true);
            _resultText.text = _sim.Won
                ? "VICTORY!\nBoss defeated in " + _sim.TurnNumber + " turn(s)."
                : "DEFEATED\nThe board reached the danger line.";
        }

        // ---------------------------------------------------------------
        // UI construction (all runtime-built - no scene-authored prefabs,
        // no sprite assets, to minimize hand-authored-without-a-compiler
        // risk for this disposable slice)
        // ---------------------------------------------------------------

        private void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRoot = canvasGo.GetComponent<RectTransform>();

            // Playfield
            _playfieldRect = CreatePanel("Playfield", canvasRoot, new Color(0.08f, 0.08f, 0.12f, 1f));
            SetAnchors(_playfieldRect, new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.82f));

            _launcherMarker = CreatePanel("LauncherMarker", _playfieldRect, new Color(0.9f, 0.9f, 0.2f, 1f));
            _launcherMarker.anchorMin = new Vector2(0.5f, 0.5f);
            _launcherMarker.anchorMax = new Vector2(0.5f, 0.5f);
            _launcherMarker.pivot = new Vector2(0.5f, 0.5f);
            _launcherMarker.sizeDelta = new Vector2(28f, 28f);

            var aimLineGo = new GameObject("AimLine", typeof(RectTransform), typeof(Image));
            aimLineGo.transform.SetParent(_playfieldRect, false);
            _aimLine = aimLineGo.GetComponent<RectTransform>();
            _aimLine.anchorMin = new Vector2(0.5f, 0.5f);
            _aimLine.anchorMax = new Vector2(0.5f, 0.5f);
            _aimLine.pivot = new Vector2(0f, 0.5f);
            _aimLineImage = aimLineGo.GetComponent<Image>();
            _aimLineImage.color = new Color(1f, 1f, 1f, 0.6f);
            _aimLineImage.enabled = false;

            // HUD strip (top)
            RectTransform hudPanel = CreatePanel("HudPanel", canvasRoot, new Color(0f, 0f, 0f, 0f));
            SetAnchors(hudPanel, new Vector2(0f, 0.90f), new Vector2(1f, 1f));

            _turnText = CreateText("TurnText", hudPanel, "Turn 0", 42f, Color.white);
            SetAnchors(_turnText.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 1f));

            _ballsText = CreateText("BallsText", hudPanel, "Ready to fire", 32f, Color.white);
            SetAnchors(_ballsText.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 1f));

            // Boss strip
            RectTransform bossPanel = CreatePanel("BossPanel", canvasRoot, new Color(0f, 0f, 0f, 0f));
            SetAnchors(bossPanel, new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.90f));

            _bossNameText = CreateText("BossNameText", bossPanel, "Slice Boss", 30f, Color.white);
            SetAnchors(_bossNameText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f));

            RectTransform bossBarBg = CreatePanel("BossBarBg", bossPanel, new Color(0.25f, 0.05f, 0.05f, 1f));
            SetAnchors(bossBarBg, new Vector2(0f, 0f), new Vector2(1f, 0.5f));

            var fillGo = new GameObject("BossBarFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(bossBarBg, false);
            _bossHpFill = fillGo.GetComponent<RectTransform>();
            _bossHpFill.anchorMin = new Vector2(0f, 0f);
            _bossHpFill.anchorMax = new Vector2(1f, 1f);
            _bossHpFill.offsetMin = Vector2.zero;
            _bossHpFill.offsetMax = Vector2.zero;
            fillGo.GetComponent<Image>().color = new Color(0.85f, 0.15f, 0.15f, 1f);

            _bossHpText = CreateText("BossHpText", bossBarBg, "800 / 800", 22f, Color.white);
            SetAnchors(_bossHpText.rectTransform, Vector2.zero, Vector2.one);

            // Result overlay
            _resultPanel = CreatePanel("ResultPanel", canvasRoot, new Color(0f, 0f, 0f, 0.75f)).gameObject;
            SetAnchors(_resultPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

            _resultText = CreateText("ResultText", _resultPanel.transform, "", 56f, Color.white);
            SetAnchors(_resultText.rectTransform, new Vector2(0.1f, 0.45f), new Vector2(0.9f, 0.75f));

            var buttonGo = new GameObject("RestartButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(_resultPanel.transform, false);
            RectTransform buttonRt = buttonGo.GetComponent<RectTransform>();
            SetAnchors(buttonRt, new Vector2(0.3f, 0.30f), new Vector2(0.7f, 0.40f));
            buttonGo.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f, 1f);
            Button restartButton = buttonGo.GetComponent<Button>();
            restartButton.onClick.AddListener(StartNewRun);

            TextMeshProUGUI buttonLabel = CreateText("Label", buttonGo.transform, "Play Again", 32f, Color.white);
            SetAnchors(buttonLabel.rectTransform, Vector2.zero, Vector2.one);
        }

        private RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<RectTransform>();
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string initialText, float fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = initialText;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            return text;
        }

        private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
