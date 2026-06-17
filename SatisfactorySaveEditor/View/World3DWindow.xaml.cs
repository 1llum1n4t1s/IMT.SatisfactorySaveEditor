using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SatisfactorySaveEditor.ViewModel;
using SatisfactorySaveParser;
using SatisfactorySaveParser.PropertyTypes.V2;
using SuperLightLogger;
using Num = System.Numerics;
using Media = System.Windows.Media;
using SDX = SharpDX;

namespace SatisfactorySaveEditor.View
{
    /// <summary>
    ///     工場を上空から自由に飛び回れる 3D ワールドビュー（第1スライス：カテゴリ別カラーの点群表示）。
    ///     セーブは一切改変しない（設置アクターの Position を読むだけ）。
    ///     座標系: Satisfactory は Z-up・cm 単位。Helix は Y-up なので (X, Z, Y)/100 [m] に変換する。
    /// </summary>
    public partial class World3DWindow : Window
    {
        private static readonly ILog log = LogManager.GetCurrentClassLogger();
        private readonly DefaultEffectsManager effects = new DefaultEffectsManager();
        private readonly HelixToolkit.Wpf.SharpDX.PerspectiveCamera camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera();

        // カテゴリ別カラー（暗背景で映える）。MapRender と対応。
        private static readonly Media.Color[] Pal =
        {
            Media.Color.FromRgb(150, 160, 170), // 0 その他
            Media.Color.FromRgb( 74, 163, 255), // 1 コンベア
            Media.Color.FromRgb(255, 210,  63), // 2 電力
            Media.Color.FromRgb( 46, 196, 182), // 3 パイプ
            Media.Color.FromRgb(176, 125,  72), // 4 鉄道
            Media.Color.FromRgb(255,  77,  79), // 5 生産
            Media.Color.FromRgb(199, 125, 255), // 6 採取
            Media.Color.FromRgb(255, 159,  28), // 7 発電
            Media.Color.FromRgb( 46, 204, 113), // 8 貯蔵
            Media.Color.FromRgb(120, 132, 140), // 9 構造
            Media.Color.FromRgb(210, 196, 150), // 10 看板・照明
            Media.Color.FromRgb(255,  94, 162), // 11 特殊建築
        };

        private static bool Important(int c) => c == 4 || c == 5 || c == 6 || c == 7 || c == 8 || c == 11;

        // カテゴリ番号 → 日本語名（詳細パネル表示用）。Pal と同順。
        private static readonly string[] CatNames =
        {
            "その他", "コンベア", "電力", "パイプ", "鉄道", "生産", "採取", "発電", "貯蔵", "構造", "看板・照明", "特殊建築",
        };

        // 中心原点・1辺1m（half-extent 0.5）の単位キューブ。各キューブは自前の 8 頂点を持つ（頂点index/8 = 序数）。
        private static readonly Num.Vector3[] CubeCorners =
        {
            new Num.Vector3(-0.5f, -0.5f, -0.5f), new Num.Vector3( 0.5f, -0.5f, -0.5f),
            new Num.Vector3( 0.5f,  0.5f, -0.5f), new Num.Vector3(-0.5f,  0.5f, -0.5f),
            new Num.Vector3(-0.5f, -0.5f,  0.5f), new Num.Vector3( 0.5f, -0.5f,  0.5f),
            new Num.Vector3( 0.5f,  0.5f,  0.5f), new Num.Vector3(-0.5f,  0.5f,  0.5f),
        };
        // 12 三角形（36 index）、CCW。上の 8 隅を参照。
        private static readonly int[] CubeTris =
        {
            0,2,1, 0,3,2,   // -Z
            4,5,6, 4,6,7,   // +Z
            0,1,5, 0,5,4,   // -Y
            3,7,6, 3,6,2,   // +Y
            1,2,6, 1,6,5,   // +X
            0,4,7, 0,7,3,   // -X
        };
        // 角方向を正規化した擬似スムーズ法線（ソリッドな塊には十分）。
        private static readonly Num.Vector3[] CubeNormals = BuildCornerNormals();
        private static Num.Vector3[] BuildCornerNormals()
        {
            var n = new Num.Vector3[8];
            for (int i = 0; i < 8; i++) n[i] = Num.Vector3.Normalize(CubeCorners[i]);
            return n;
        }

        // ヒットテスト用：立方体序数 → SaveEntity（頂点/8・三角形/12・index-start/36 が序数に一致）。
        private SaveEntity[] cubeEntities;
        private MeshGeometryModel3D boxModel; // 全カテゴリを統合した箱モデル（1 ドローコール）
        private int selectedCubeIndex = -1;   // 現在選択中の立方体序数（Delete/Ctrl+D の対象。OnViewportLeftUp で設定）
        // 選択された対象を強調表示する常駐ハイライト（ヒットテスト対象外）。
        private PointGeometryModel3D highlight;
        private System.Windows.Point pointerDownPos;
        private bool pointerDownIsLeft;

        public World3DWindow(IEnumerable<SaveObject> entries)
        {
            InitializeComponent();

            try
            {
                viewport.EffectsManager = effects;
                viewport.Camera = camera;

                // FPS 飛行: WalkAround は視点をカメラ位置で回す（首振り）。WASD で並進移動。
                viewport.CameraMode = CameraMode.WalkAround;
                viewport.CameraRotationMode = CameraRotationMode.Turntable; // 水平を保ち地平線が傾かない

                // WASD 移動（CameraController.OnKeyDown に内蔵。IsMoveEnabled + 速度のみ要設定）。
                // W/S=前後, A/D=左右, Q/Z=上下, Ctrl=低速。7km スケールなので既定 1.0 は遅すぎる。
                viewport.IsMoveEnabled = true;
                viewport.MoveSensitivity = 100;          // 前後速度（50〜200 で調整可）
                viewport.LeftRightPanSensitivity = 100;  // A/D 横移動・Q/Z 上下はこちらを参照
                viewport.UpDownPanSensitivity = 100;
                viewport.IsRotationEnabled = true;
                viewport.IsPanEnabled = true;
                // ホイールズーム: 3.1.2 は OnMouseWheel が 0.001f をハードコードし ZoomSensitivity を無視する。
                // さらに指数ドリー(2.5^delta)なので 7km スケールで 1 ノッチが巨大化＝"通り越す"。
                // → 既定の指数ズームは殺し、下の PreviewMouseWheel で「線形・固定 m/ノッチ」に置き換える。
                viewport.IsZoomEnabled = true;
                viewport.ZoomAroundMouseDownPoint = false; // 視線方向ドリー（カーソル先ではなく）＝スケールで安定

                // アンチエイリアス（"荒い" の主因。MSAA 既定 Disable）。RDP/WARP コストを考え Four 固定。
                viewport.MSAA = MSAALevel.Four;
                viewport.FXAALevel = FXAALevel.Low;      // 点・線スプライトの縁を整える（MSAA が効きにくい所）
                viewport.EnableSSAO = false;             // 点群では SSAO はノイズになるだけ
                // EnableSwapChainRendering は既定 false（DPFCanvas/D3DImage 経路＝RDP 安全）のまま触らない。

                viewport.BackgroundColor = Media.Color.FromRgb(28, 30, 34);
                viewport.ShowCoordinateSystem = true;

                // 左ボタンはカメラ制御に一切使わせない（＝選択専用）。ジェスチャを全面的に握る。
                // Helix 3.1.2 はカメラ操作を全部「右ボタン」に割り当てており、左は元から無バインド。
                // 明示的に「右ドラッグ=回転 / Shift+右=パン」だけ残す（左クリックはピック専用）。
                viewport.UseDefaultGestures = false;
                viewport.InputBindings.Clear();
                viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Rotate, new MouseGesture(MouseAction.RightClick)));
                viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Pan, new MouseGesture(MouseAction.RightClick, ModifierKeys.Shift)));
                // ホイールズームはバインド不要（IsZoomEnabled=true で OnMouseWheel が処理）。

                camera.UpDirection = new Vector3D(0, 1, 0);
                camera.NearPlaneDistance = 1.0;          // 0.5→1.0
                camera.FarPlaneDistance = 100000;        // 5,000,000→100,000（near/far 比 1e7 は精度・z-fighting の主因）

                // エディタ風ライティング: 低めの寒色アンビエント + キー + 逆側フィル（箱メッシュ描画にのみ効く）。
                viewport.Items.Add(new AmbientLight3D { Color = Media.Color.FromRgb(90, 95, 100) });
                viewport.Items.Add(new DirectionalLight3D { Direction = new Vector3D(-0.5, -1.0, -0.3), Color = Media.Color.FromRgb(255, 250, 240) });
                viewport.Items.Add(new DirectionalLight3D { Direction = new Vector3D(0.5, -0.3, 0.6), Color = Media.Color.FromRgb(70, 80, 95) });

                BuildScene(entries);

                // 左クリック=選択（押下→離上の移動量で「クリック」か「ドラッグ」かを判定）。
                viewport.PreviewMouseLeftButtonDown += OnViewportLeftDown;
                viewport.PreviewMouseLeftButtonUp += OnViewportLeftUp;
                // ホイールは完全に自前処理（固定 m/ノッチの線形ドリー）。Helix の指数ズームは走らせない。
                viewport.PreviewMouseWheel += OnViewportWheel;

                log.Info($"3D window initialized. Adapter/EffectsManager ready={effects != null}");
            }
            catch (Exception ex)
            {
                log.Error("3D window init failed: " + ex);
                MessageBox.Show("3Dビューの初期化に失敗しました:\n" + ex.Message, "3D View", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // WASD を開いた直後から効かせる（初回クリック前でもキーボードフォーカスを viewport に渡す）。
            // viewport は mouse-down で自動フォーカスもするので、失っても 3D 内クリックで復帰する。
            viewport.Focusable = true;
            Loaded += (s, e) => { Keyboard.Focus(viewport); viewport.Focus(); };

            // Delete=削除 / Ctrl+D=複製。PreviewKeyDown（トンネル）なので viewport より先に拾える。
            // 素の W/A/S/D は横取りしないので viewport の WASD 移動はそのまま効く。
            PreviewKeyDown += OnWindowKeyDown;

            Closed += (s, e) => effects.Dispose();
        }

        private static int CategoryOf(string seg)
        {
            bool C(string sub) => seg.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
            if (C("Conveyor")) return 1;
            if (C("PowerLine") || C("PowerPole") || C("PowerTower") || C("PowerSwitch") || C("PowerStorage")) return 2;
            if (C("Pipeline") || C("PipeHyper") || C("Hypertube") || C("HyperTube") || C("Valve") || C("PipeStorage")) return 3;
            if (C("Railroad") || C("Train")) return 4;
            if (C("Constructor") || C("Assembler") || C("Manufacturer") || C("Smelter") || C("Foundry")
                || C("Refinery") || C("Blender") || C("Converter") || C("HadronCollider")
                || C("QuantumEncoder") || C("Packager") || C("Mam") || C("WorkBench") || C("Workshop")) return 5;
            if (C("Miner") || C("OilPump") || C("WaterPump") || C("Fracking") || C("ResourceExtractor")) return 6;
            if (C("Generator") || C("Nuclear") || C("AlienPower")) return 7;
            if (C("Storage") || C("IndustrialTank") || C("CentralStorage")) return 8;
            if (C("Foundation") || C("Wall") || C("Beam") || C("Frame") || C("Pillar") || C("Ramp")
                || C("Stair") || C("Roof") || C("Catwalk") || C("Walkway") || C("Ladder") || C("Gate")
                || C("Passthrough") || C("Fence")) return 9;
            if (C("Sign") || C("Light") || C("Floodlight") || C("StreetLight")) return 10;
            if (C("SpaceElevator") || C("TradingPost") || C("HubTerminal") || C("ResourceSink")
                || C("DroneStation") || C("TruckStation") || C("RadarTower") || C("Portal")
                || C("Drone") || C("Vehicle")) return 11;
            return 0;
        }

        // 構造物（カテゴリ9）のローカル実寸[m]をクラス名から解決する。X=幅 Y=奥行 Z=厚み/高さ（ゲーム軸）。
        // クラス名の NxM 数字はプレフィックスで意味が変わる: Foundation=8x8xM板 / Wall=Nx0.3xM薄板 / Ramp=Nx8xM。
        // 非構造・未マッチは 1×1×1 を返し、従来どおり単位キューブで描く。Scale は呼び出し側で乗算する。
        private static readonly System.Text.RegularExpressions.Regex NxM =
            new System.Text.RegularExpressions.Regex(@"(\d+)x(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static Num.Vector3 SizeOfGame(string seg)
        {
            bool C(string s) => seg.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            var m = NxM.Match(seg);
            float n = m.Success ? int.Parse(m.Groups[1].Value) : 0f; // 第1数字（幅）
            float k = m.Success ? int.Parse(m.Groups[2].Value) : 0f; // 第2数字（高さ/厚み）

            if (C("FoundationPassthrough")) return new Num.Vector3(8, 8, 4);            // Lift/Pipe ホール
            if (C("Foundation"))            return new Num.Vector3(8, 8, k > 0 ? k : 1); // 8x8xH 板
            if (C("Ramp"))                  return new Num.Vector3(n > 0 ? n : 8, 8, k > 0 ? k : 4);
            if (C("Wall"))                  return new Num.Vector3(n > 0 ? n : 8, 0.3f, k > 0 ? k : 4);
            if (C("Beam"))                  return new Num.Vector3(0.4f, 2, 0.4f);
            if (C("Pillar"))                return new Num.Vector3(2, 2, 2);
            if (C("Ladder"))                return new Num.Vector3(1, 0.3f, 4);
            if (C("Catwalk") || C("Walkway")) return new Num.Vector3(4, 1, 0.3f);
            if (C("Floodlight"))            return new Num.Vector3(1, 0.5f, 1);
            return new Num.Vector3(1, 1, 1); // 既定（非構造・未マッチ）
        }

        private void BuildScene(IEnumerable<SaveObject> entries)
        {
            var byCat = new List<Num.Vector3>[Pal.Length];
            var entByCat = new List<SaveEntity>[Pal.Length]; // byCat と同順で実体を保持
            for (var i = 0; i < byCat.Length; i++) { byCat[i] = new List<Num.Vector3>(); entByCat[i] = new List<SaveEntity>(); }
            int total = 0, excluded = 0;
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var obj in entries)
            {
                if (!(obj is SaveEntity ent)) continue;
                var p = ent.Position;
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z)) continue;
                if (Math.Abs(p.X) < 1f && Math.Abs(p.Y) < 1f) continue;
                // 水平 ±200km / 高さ ±5km を超える異常配置（飛行中の乗り物・projectile 等）はカメラ枠を壊すので除外。
                if (Math.Abs(p.X) > 2e7f || Math.Abs(p.Y) > 2e7f || Math.Abs(p.Z) > 5e5f) { excluded++; continue; }

                var seg = obj.TypePath ?? "";
                var slash = seg.LastIndexOf('/'); if (slash >= 0) seg = seg.Substring(slash + 1);
                var dot = seg.LastIndexOf('.'); if (dot >= 0) seg = seg.Substring(dot + 1);
                int cat = CategoryOf(seg);

                // cm→m, Z-up→Y-up: (X, Z, Y)/100
                var v = new Num.Vector3(p.X / 100f, p.Z / 100f, p.Y / 100f);
                byCat[cat].Add(v);
                entByCat[cat].Add(ent); // 同じインデックスで実体を控える
                total++;
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }

            if (total == 0)
            {
                infoText.Text = "設置物が見つかりませんでした";
                log.Warn("3D: no placed entities found");
                return;
            }

            // 全オブジェクトを 1m³ の箱として 1 メッシュ・1 ドローコールに統合（頂点色でカテゴリ色を焼き込む）。
            BuildBoxMesh(byCat, entByCat, total);

            // 接続オーバーレイ（ベルト/パイプのスプライン + 電線）。読み取り専用＝RawData は触らない。
            BuildConnections(entByCat);

            // 地面＋グリッド（シーン境界からサイズ算出。minY=Helix 上方向の最小=最低オブジェクト高）
            AddGroundAndGrid(minX, maxX, minY, minZ, maxZ);

            // バウンディングボックスからカメラを明示配置（ZoomExtents に頼らない）
            var cx = (minX + maxX) / 2f;
            var cy = (minY + maxY) / 2f;
            var cz = (minZ + maxZ) / 2f;
            var span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            if (span <= 0) span = 1000;

            // 中心の上空・斜め後方から見下ろす
            var camPos = new Point3D(cx, cy + span * 1.1, cz - span * 1.1);
            camera.Position = camPos;
            camera.LookDirection = new Vector3D(cx - camPos.X, cy - camPos.Y, cz - camPos.Z);
            camera.UpDirection = new Vector3D(0, 1, 0);

            // 選択ハイライト（最初は非表示・ヒットテスト対象外）。選択時に対象点へ移動・表示する。
            highlight = new PointGeometryModel3D
            {
                Geometry = new PointGeometry3D
                {
                    Positions = new Vector3Collection(new[] { new Num.Vector3(cx, cy, cz) }),
                    Indices = new IntCollection(new[] { 0 })
                },
                Color = Media.Colors.Cyan,
                Size = new Size(20, 20),
                Figure = PointFigure.Ellipse,
                IsHitTestVisible = false,
                Visibility = Visibility.Hidden
            };
            viewport.Items.Add(highlight);

            infoText.Text = $"設置物 {total:N0} 件 を 3D 表示中";
            log.Info($"3D scene: total={total}, excluded={excluded}, bounds X[{minX:0}..{maxX:0}] Y[{minY:0}..{maxY:0}] Z[{minZ:0}..{maxZ:0}] span={span:0}, camPos=({camPos.X:0},{camPos.Y:0},{camPos.Z:0})");
        }

        // 地面（薄い箱メッシュ）＋グリッド線を、シーン境界から算出して配置する。
        // DepthBias で床を奥へ押し、点群・グリッドが z-fighting しないようにする。
        private void AddGroundAndGrid(float minX, float maxX, float minY, float minZ, float maxZ)
        {
            float pad = Math.Max(maxX - minX, maxZ - minZ) * 0.25f + 500f; // 工場より広めに +25% +500m
            float gx0 = minX - pad, gx1 = maxX + pad;
            float gz0 = minZ - pad, gz1 = maxZ + pad;
            float sizeX = gx1 - gx0, sizeZ = gz1 - gz0;
            float groundTop = minY - 2f; // 最低オブジェクトの 2m 下に床面を置く

            // 1) 床（平面クアッド。HelixToolkit.SharpDX のメッシュ型を直接構築する）
            var up = new Num.Vector3(0, 1, 0);
            var ground = new MeshGeometryModel3D
            {
                Geometry = new HelixToolkit.SharpDX.MeshGeometry3D
                {
                    Positions = new Vector3Collection(new[]
                    {
                        new Num.Vector3(gx0, groundTop, gz0),
                        new Num.Vector3(gx1, groundTop, gz0),
                        new Num.Vector3(gx1, groundTop, gz1),
                        new Num.Vector3(gx0, groundTop, gz1),
                    }),
                    Indices = new IntCollection(new[] { 0, 1, 2, 0, 2, 3 }),
                    Normals = new Vector3Collection(new[] { up, up, up, up }),
                },
                Material = new PhongMaterial
                {
                    DiffuseColor = Media.Color.FromRgb(28, 32, 38).ToColor4(),
                    AmbientColor = Media.Color.FromRgb(16, 19, 24).ToColor4(),
                    SpecularColor = Media.Color.FromRgb(0, 0, 0).ToColor4(),
                    SpecularShininess = 1f,
                    EmissiveColor = Media.Color.FromRgb(5, 6, 8).ToColor4(), // 完全な黒に落ちない自己発光
                },
                CullMode = SDX.Direct3D11.CullMode.None, // 上下どちらからも見える
                IsThrowingShadow = false,
                DepthBias = -64,
                SlopeScaledDepthBias = -2.0,
            };
            viewport.Items.Add(ground);

            // 2) グリッド線（手動ステップ。GenerateGrid は固定ステップ 1.0 で km スケールに不向き）
            float gridY = groundTop + 0.5f;
            float step = NiceGridStep(Math.Max(sizeX, sizeZ)); // 7km シーンで約 250m
            var lb = new LineBuilder();
            for (float x = (float)(Math.Ceiling(gx0 / step) * step); x <= gx1; x += step)
                lb.AddLine(new Num.Vector3(x, gridY, gz0), new Num.Vector3(x, gridY, gz1));
            for (float z = (float)(Math.Ceiling(gz0 / step) * step); z <= gz1; z += step)
                lb.AddLine(new Num.Vector3(gx0, gridY, z), new Num.Vector3(gx1, gridY, z));
            var grid = new LineGeometryModel3D
            {
                Geometry = lb.ToLineGeometry3D(),
                Color = Media.Color.FromArgb(120, 90, 100, 110),
                Thickness = 0.8,
                FixedSize = false,
                IsThrowingShadow = false,
                DepthBias = -96,
            };
            viewport.Items.Add(grid);
        }

        // "綺麗な" グリッド間隔（1/2/5×10^n、約 30 分割）。
        private static float NiceGridStep(float span)
        {
            float raw = span / 30f;
            float mag = (float)Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1f))));
            float norm = raw / mag;
            float nice = norm < 1.5f ? 1f : norm < 3.5f ? 2f : norm < 7.5f ? 5f : 10f;
            return nice * mag;
        }

        // 全オブジェクトを 1m³（=1 Helix 単位）の立方体として 1 メッシュに統合する。
        // 各立方体は自前の 8 頂点・36 index を専有し、頂点/8・三角形/12・index-start/36 が立方体序数に一致する
        // ＝ヒットテスト結果からクリックされた立方体（= SaveEntity）を逆引きできる。色は頂点カラーで焼き込む。
        private void BuildBoxMesh(List<Num.Vector3>[] byCat, List<SaveEntity>[] entByCat, int total)
        {
            int cubeCount = total;
            var positions = new Vector3Collection(cubeCount * 8);
            var normals = new Vector3Collection(cubeCount * 8);
            var colors = new Color4Collection(cubeCount * 8);
            var indices = new IntCollection(cubeCount * 36);
            cubeEntities = new SaveEntity[cubeCount];

            // パレット色を一度だけ Maths.Color4 へ（Media.Color.ToColor4() 拡張、地面でも使用済み）。
            var palC4 = new HelixToolkit.Maths.Color4[Pal.Length];
            for (int c = 0; c < Pal.Length; c++) palC4[c] = Pal[c].ToColor4();

            int cube = 0;
            for (int cat = 0; cat < entByCat.Length; cat++)
            {
                var ents = entByCat[cat];
                var col = palC4[cat];
                for (int k = 0; k < ents.Count; k++)
                {
                    var ent = ents[k];
                    var p = ent.Position; // cm, ゲーム Z-up（生の中心）
                    // 構造物は実寸＋回転、それ以外は単位キューブ。8頂点/36index の序数不変条件は保たれる。
                    EmitCubeGeometry(ent, p.X / 100f, p.Y / 100f, p.Z / 100f, cat, LastSeg(ent.TypePath), col,
                                     positions, normals, colors, indices);
                    cubeEntities[cube] = ent;
                    cube++;
                }
            }

            var mesh = new HelixToolkit.SharpDX.MeshGeometry3D
            {
                Positions = positions,
                Indices = indices,
                Normals = normals,
                Colors = colors, // 頂点色。VertexColorBlendingFactor=1 で採用される
            };
            mesh.IsDynamic = true; // 後続の単体編集（削除/複製）でバッファを再マップ＝RDP/WARP で安価

            boxModel = new MeshGeometryModel3D
            {
                Geometry = mesh,
                Material = new PhongMaterial
                {
                    DiffuseColor = Media.Color.FromRgb(255, 255, 255).ToColor4(), // 白ベース。色相は頂点色が駆動
                    AmbientColor = Media.Color.FromRgb(40, 44, 50).ToColor4(),
                    SpecularColor = Media.Color.FromRgb(20, 20, 20).ToColor4(),
                    SpecularShininess = 4f,
                    VertexColorBlendingFactor = 1.0, // mesh.Colors を採用（DiffuseColor ではなく）
                },
                CullMode = SDX.Direct3D11.CullMode.Back, // ソリッド箱。背面カリングでオーバードロー半減
                IsThrowingShadow = false,
                IsHitTestVisible = true,
            };
            viewport.Items.Add(boxModel);
        }

        // 立方体1個分の8頂点・36indexを指定コレクションへ追記する（構築 BuildBoxMesh と複製 AppendCube の共通経路）。
        // gx/gy/gz は生のゲーム中心[m]（Z-up）。構造物(cat==9)は SizeOfGame×Scale で実寸化し、ゲーム空間で
        // クォータニオン回転してから (X,Z,Y) スワップで Helix(Y-up) 系へ。非構造は size=1×1×1・回転恒等で従来と一致。
        // baseV は追記先 positions の現在長＝立方体序数×8 となり、序数不変条件（頂点/8・index-start/36）を維持する。
        private static void EmitCubeGeometry(
            SaveEntity ent, float gx, float gy, float gz, int cat, string seg, HelixToolkit.Maths.Color4 col,
            Vector3Collection positions, Vector3Collection normals, Color4Collection colors, IntCollection indices)
        {
            // サイズ: 構造のみ実寸、他は単位。Scale を component 乗算（cat9 は実測 1,1,1 だが正しさのため掛ける）。
            var size = cat == 9 ? SizeOfGame(seg) : new Num.Vector3(1, 1, 1);
            var sc = ent.Scale;
            if (sc != null) size = new Num.Vector3(size.X * sc.X, size.Y * sc.Y, size.Z * sc.Z);

            // 回転: ゲーム空間のクォータニオン。長さ0付近は恒等にフォールバック。
            var q = Num.Quaternion.Identity;
            var rr = ent.Rotation;
            if (rr != null)
            {
                var cand = new Num.Quaternion(rr.X, rr.Y, rr.Z, rr.W);
                if (cand.LengthSquared() > 1e-6f) q = Num.Quaternion.Normalize(cand);
            }

            int baseV = positions.Count; // 追記先の先頭頂点 index（= 立方体序数×8）
            for (int v = 0; v < 8; v++)
            {
                // ローカル隅[m]（ゲーム軸）→ 回転 → ゲーム中心へ平行移動 → 最後に (X,Z,Y) スワップ。
                var local = new Num.Vector3(CubeCorners[v].X * size.X,
                                            CubeCorners[v].Y * size.Y,
                                            CubeCorners[v].Z * size.Z);
                var rot = Num.Vector3.Transform(local, q);
                positions.Add(new Num.Vector3(gx + rot.X, gz + rot.Z, gy + rot.Y));
                var n = Num.Vector3.Transform(CubeNormals[v], q);
                normals.Add(new Num.Vector3(n.X, n.Z, n.Y));
                colors.Add(col);
            }
            for (int t = 0; t < CubeTris.Length; t++) indices.Add(baseV + CubeTris[t]);
        }

        // ホイール 1 ノッチ＝固定の m 移動（線形）。Helix 3.1.2 の指数ズーム(2.5^delta)＋慣性テールを置き換える。
        // camera.Position/LookDirection は WPF Media3D 型（このファイルの既存コードと同じ）。
        private void OnViewportWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true; // Helix の OnMouseWheel を走らせない（指数ズーム・慣性テールを無効化）
            if (camera == null) return;

            int notches = e.Delta / 120;            // 標準ホイール = ±120/ノッチ
            if (notches == 0) notches = e.Delta > 0 ? 1 : -1;

            // 1 ノッチあたりの移動量[m]。Ctrl で微調整（接近時の寄せ）。シーンは m 単位。
            double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 6.0 : 25.0;

            var look = camera.LookDirection;        // Media3D.Vector3D
            double len = look.Length;
            if (len < 1e-6) return;
            look = new Vector3D(look.X / len, look.Y / len, look.Z / len);

            var move = look * (step * notches);     // ホイール上=前進（視線方向）。距離非依存の線形。
            camera.Position = new Point3D(
                camera.Position.X + move.X,
                camera.Position.Y + move.Y,
                camera.Position.Z + move.Z);
            viewport.InvalidateRender();
        }

        private void OnViewportLeftDown(object sender, MouseButtonEventArgs e)
        {
            pointerDownPos = e.GetPosition(viewport);
            pointerDownIsLeft = true;
        }

        private void OnViewportLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (!pointerDownIsLeft) return;
            pointerDownIsLeft = false;

            var up = e.GetPosition(viewport);
            if ((up - pointerDownPos).Length > 5) return; // ドラッグ（=移動操作）だったのでピックしない

            try
            {
                var hits = viewport.FindHits(up); // Distance 昇順
                // 統合キューブモデルへの最初の有効ヒット。
                var hit = hits?.FirstOrDefault(h => h.IsValid && ReferenceEquals(h.ModelHit, boxModel));
                if (hit == null || cubeEntities == null) { ClearHighlight(); ClearDetails(); return; }

                // merged-mesh 不変条件: キューブ i は 頂点[i*8..i*8+7] / index[i*36..i*36+35] を専有。
                // TriangleIndices = ヒット三角形の 3 頂点 index（3 つとも同じキューブ序数）。
                int cubeIndex = -1;
                var tri = hit.TriangleIndices;
                if (tri != null)
                    cubeIndex = tri.Item1 / 8;
                else if (hit.IndiceStartLocation >= 0) // TriangleIndices が null の場合のフォールバック
                    cubeIndex = hit.IndiceStartLocation / 36;

                if (cubeIndex < 0 || cubeIndex >= cubeEntities.Length) { ClearHighlight(); return; }

                var ent = cubeEntities[cubeIndex];
                if (ent == null) { ClearHighlight(); ClearDetails(); selectedCubeIndex = -1; return; } // 縮退削除済みは選択不可
                selectedCubeIndex = cubeIndex; // Delete/Ctrl+D の対象として保持

                MoveHighlight(hit.PointHit); // PointHit は System.Numerics.Vector3（既存シグネチャと一致）
                SelectInEditor(ent);
                ShowDetails(ent);            // 3D ウィンドウ内に詳細情報を表示
            }
            catch (Exception ex)
            {
                log.Warn("3D pick failed: " + ex);
            }
        }

        private void MoveHighlight(Num.Vector3 worldPoint)
        {
            if (highlight == null) return;
            highlight.Geometry = new PointGeometry3D
            {
                Positions = new Vector3Collection(new[] { worldPoint }),
                Indices = new IntCollection(new[] { 0 })
            };
            highlight.Visibility = Visibility.Visible;
        }

        private void ClearHighlight()
        {
            if (highlight != null) highlight.Visibility = Visibility.Hidden;
        }

        /// <summary>
        ///     クリックされた SaveEntity を、メイン側ツリー＋右ペインで選択させる。
        ///     InstanceName で一意特定し、MainViewModel の公開 API を叩く（私有フィールド直叩きはしない）。
        /// </summary>
        private void SelectInEditor(SaveEntity ent)
        {
            var name = ent?.InstanceName;
            if (string.IsNullOrEmpty(name)) return;

            var mvm = Application.Current?.MainWindow?.DataContext as MainViewModel;
            if (mvm == null) return;

            mvm.SelectByInstanceName(name);
        }

        // 詳細パネル：クリックしたオブジェクトの identity（フレンドリー名・分類・クラス・ID・座標・向き・スケール）を表示。
        private void ShowDetails(SaveEntity ent)
        {
            if (ent == null) { ClearDetails(); return; }

            detailTitle.Text = SatisfactorySaveEditor.Util.FriendlyName.Pretty(ent.TypePath);

            var seg = LastSeg(ent.TypePath);
            int cat = CategoryOf(seg);
            var p = ent.Position ?? new SatisfactorySaveParser.Structures.Vector3();
            var s = ent.Scale ?? new SatisfactorySaveParser.Structures.Vector3 { X = 1, Y = 1, Z = 1 };
            var r = ent.Rotation ?? new SatisfactorySaveParser.Structures.Vector4 { W = 1 };
            // クォータニオン → Z 軸まわりのヨー角[deg]（Satisfactory は Z-up）。
            double yaw = Math.Atan2(2.0 * (r.W * r.Z + r.X * r.Y), 1.0 - 2.0 * (r.Y * r.Y + r.Z * r.Z)) * 180.0 / Math.PI;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"分類: {CatName(cat)}");
            sb.AppendLine($"クラス: {seg}");
            sb.AppendLine($"ID: {ShortId(ent.InstanceName)}");
            sb.AppendLine($"位置 (cm): X={p.X:0} Y={p.Y:0} Z={p.Z:0}");
            sb.Append($"向き: {yaw:0.#}°   スケール: {s.X:0.##}, {s.Y:0.##}, {s.Z:0.##}");
            detailBody.Text = sb.ToString();
            detailPanel.Visibility = Visibility.Visible;
        }

        private static string CatName(int c) => (c >= 0 && c < CatNames.Length) ? CatNames[c] : "その他";

        // InstanceName の末尾 '.' 以降（実インスタンス名）だけを返す（長いパス全体は省く）。
        private static string ShortId(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName)) return "";
            var dot = instanceName.LastIndexOf('.');
            return dot >= 0 ? instanceName.Substring(dot + 1) : instanceName;
        }

        private void ClearDetails()
        {
            if (detailPanel != null) detailPanel.Visibility = Visibility.Collapsed;
        }

        // Delete=削除 / Ctrl+D=複製。素の WASD は横取りしない（viewport が処理）。
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                DuplicateSelected();
                e.Handled = true;
            }
        }

        // 選択中オブジェクトを削除：メッシュ表示（縮退）＋エディタ/パーサ（保存の真実）の双方を同期。
        private void DeleteSelected()
        {
            if (selectedCubeIndex < 0 || cubeEntities == null || selectedCubeIndex >= cubeEntities.Length) return;
            var ent = cubeEntities[selectedCubeIndex];
            if (ent == null) return;

            DeleteCube(selectedCubeIndex);  // 1) 3D 表示を縮退（序数は不変＝他キューブのピック不変）
            ClearHighlight();               // 2) ハイライト解除
            ClearDetails();                 //    詳細パネルも閉じる

            var mvm = Application.Current?.MainWindow?.DataContext as MainViewModel;
            mvm?.DeleteByInstanceName(ent.InstanceName); // 3) Model+パーサ Entries から削除（保存に反映）

            selectedCubeIndex = -1;
        }

        // 選択中オブジェクトを複製：クローン（新 InstanceName・RawData 逐語コピー・+3m X オフセット）を作り、
        // 3D に立方体を追加してクローンを選択状態にする。
        private void DuplicateSelected()
        {
            if (selectedCubeIndex < 0 || cubeEntities == null || selectedCubeIndex >= cubeEntities.Length) return;
            var src = cubeEntities[selectedCubeIndex];
            if (src == null) return;

            var mvm = Application.Current?.MainWindow?.DataContext as MainViewModel;
            if (mvm == null) return;

            var clone = mvm.DuplicateByInstanceName(src.InstanceName); // 1) Model+パーサで複製（+300cm X）
            if (clone == null) return;

            var p = clone.Position; // cm, Z-up
            var centerM = new Num.Vector3(p.X / 100f, p.Z / 100f, p.Y / 100f); // m, Y-up（ハイライト用）
            int cat = CategoryOf(LastSeg(clone.TypePath));
            int newIdx = AppendCube(clone, cat);                          // 2) 3D に立方体追加（実寸・回転）

            MoveHighlight(centerM);                                       // 3) ハイライトをクローンへ
            selectedCubeIndex = newIdx;                                   // 4) 選択をクローンへ
            mvm.SelectByInstanceName(clone.InstanceName);                 // 5) ツリー＋右ペインをクローンへ
            ShowDetails(clone);                                           // 6) 詳細パネルをクローンへ
        }

        // 立方体 1 個を縮退（8 頂点を中心へ・36 index を 1 点へ）→ 非表示・非選択化。序数 i は不変。
        private void DeleteCube(int cubeIndex)
        {
            if (boxModel == null || cubeEntities == null) return;
            if (cubeIndex < 0 || cubeIndex >= cubeEntities.Length) return;
            var mesh = boxModel.Geometry as HelixToolkit.SharpDX.MeshGeometry3D;
            if (mesh == null) return;

            int baseV = cubeIndex * 8;
            var c = new Num.Vector3(0, 0, 0);
            for (int v = 0; v < 8; v++) c += mesh.Positions[baseV + v];
            c /= 8f; // 中心は有限値 → UpdateBounds は安全
            for (int v = 0; v < 8; v++) mesh.Positions[baseV + v] = c;

            int baseI = cubeIndex * 36;
            for (int t = 0; t < 36; t++) mesh.Indices[baseI + t] = baseV;

            cubeEntities[cubeIndex] = null; // OnViewportLeftUp の null ガードで再選択不可

            mesh.UpdateVertices();   // 頂点バッファ再アップロード
            mesh.UpdateTriangles();  // インデックスバッファ再アップロード
            mesh.UpdateBounds();     // CPU 側 AABB
            mesh.UpdateOctree(true);
            viewport.InvalidateRender();
        }

        // 立方体 1 個を末尾序数に追加（BuildBoxMesh と同じ実寸・回転経路）。戻り値: 追加した序数。
        private int AppendCube(SaveEntity ent, int cat)
        {
            if (boxModel == null) return -1;
            var mesh = boxModel.Geometry as HelixToolkit.SharpDX.MeshGeometry3D;
            if (mesh == null) return -1;
            if (cat < 0 || cat >= Pal.Length) cat = 0;

            int newIndex = cubeEntities != null ? cubeEntities.Length : 0;
            var p = ent.Position; // cm, ゲーム Z-up（生）
            EmitCubeGeometry(ent, p.X / 100f, p.Y / 100f, p.Z / 100f, cat, LastSeg(ent.TypePath), Pal[cat].ToColor4(),
                             mesh.Positions, mesh.Normals, mesh.Colors, mesh.Indices);

            var grown = new SaveEntity[newIndex + 1]; // 固定長配列を 1 個伸ばしてコピー
            if (cubeEntities != null) Array.Copy(cubeEntities, grown, newIndex);
            grown[newIndex] = ent;
            cubeEntities = grown;

            mesh.UpdateVertices();
            mesh.UpdateTriangles();
            mesh.UpdateBounds();
            mesh.UpdateOctree(true);
            viewport.InvalidateRender();
            return newIndex;
        }

        // ───────────────────────── 接続オーバーレイ（読み取り専用） ─────────────────────────
        // ベルト/パイプの mSplineData（ローカルスプライン点）と PowerLine の mWireInstances（絶対ワールド端点）を
        // 既存 V2 リーダーで取り出し、カテゴリ毎に 1 本の統合ラインメッシュとして描く。RawData は一切改変しない。

        // (X,Z,Y)/100 [m] ヘリックス変換（箱の位置と同一）。cm in → m out。
        private static Num.Vector3 ToHelix(float cmX, float cmY, float cmZ)
            => new Num.Vector3(cmX / 100f, cmZ / 100f, cmY / 100f);

        // アクター/コンポーネントの RawData をフレーミングして V2 プロパティ列を得る。失敗時 null（呼び出し側でスキップ）。
        // V2FramingAnalysis と同じ最小スキップ探索。親持ちアクターの未解決前置（docs §7）は parse 失敗→null に落ちる。
        private static PropertiesListV2 FrameProps(SaveObject obj)
        {
            var raw = obj.RawData;
            if (raw == null || raw.Length == 0) return null;
            bool isComp = obj is SaveComponent;
            bool hasParent = false;
            int basePos = 0;
            try
            {
                using var ms = new System.IO.MemoryStream(raw);
                using var r = new System.IO.BinaryReader(ms);
                if (!isComp)
                {
                    var pr = r.ReadLengthPrefixedString();
                    var pn = r.ReadLengthPrefixedString();
                    hasParent = (pr != null && pr.Length > 0) || (pn != null && pn.Length > 0);
                    int cc = r.ReadInt32();
                    if (cc < 0 || cc > 100000) return null;
                    for (int i = 0; i < cc; i++) { r.ReadLengthPrefixedString(); r.ReadLengthPrefixedString(); }
                    basePos = (int)ms.Position;
                }
            }
            catch { return null; }

            int[] cands = isComp ? new[] { 1, 0, 2 }
                                 : (hasParent ? new[] { 3, 1, 2, 0 } : new[] { 1, 3, 0, 2 });
            foreach (var skip in cands)
            {
                int start = basePos + skip;
                if (start < 0 || start > raw.Length) continue;
                var slice = new byte[raw.Length - start];
                System.Array.Copy(raw, start, slice, 0, slice.Length);
                try { return PropertiesListV2.Parse(slice); } catch { }
            }
            return null;
        }

        // mSplineData 本体（[int32 count] + 要素毎の内部プロパティ列）から Location（ローカル cm, double xyz）を抽出。
        // 各要素は Location/ArriveTangent/LeaveTangent(各24B) → "None"。直線ポリラインなので Location のみ拾う。
        private static List<Num.Vector3> DecodeSplineLocal(byte[] body)
        {
            var pts = new List<Num.Vector3>();
            using var ms = new System.IO.MemoryStream(body);
            using var r = new System.IO.BinaryReader(ms);
            int count = r.ReadInt32();
            if (count < 0 || count > 100000) return pts;
            for (int i = 0; i < count; i++)
            {
                while (ms.Position < ms.Length)
                {
                    var tag = FPropertyTagV2.Read(r);
                    if (tag.IsTerminator) break;                 // 要素内 "None" 終端
                    if (tag.BinarySize < 0 || ms.Position + tag.BinarySize > ms.Length) return pts;
                    var b = r.ReadBytes(tag.BinarySize);
                    if (tag.Name == "Location" && b.Length >= 24)
                    {
                        double x = BitConverter.ToDouble(b, 0), y = BitConverter.ToDouble(b, 8), z = BitConverter.ToDouble(b, 16);
                        pts.Add(new Num.Vector3((float)x, (float)y, (float)z)); // ローカル cm（ゲーム軸）
                    }
                }
            }
            return pts;
        }

        // mWireInstances 本体から各 WireInstance の Locations（ワールド cm, double）を2点抽出して端点ペアにする。
        // 各要素は Locations×2 + CachedRelativeLocations×2 → "None"。絶対座標の Locations のみ拾う。
        private static List<(Num.Vector3 a, Num.Vector3 b)> DecodeWires(byte[] body)
        {
            var wires = new List<(Num.Vector3, Num.Vector3)>();
            using var ms = new System.IO.MemoryStream(body);
            using var r = new System.IO.BinaryReader(ms);
            int count = r.ReadInt32();
            if (count < 0 || count > 100000) return wires;
            for (int i = 0; i < count; i++)
            {
                var locs = new List<Num.Vector3>();
                while (ms.Position < ms.Length)
                {
                    var tag = FPropertyTagV2.Read(r);
                    if (tag.IsTerminator) break;
                    if (tag.BinarySize < 0 || ms.Position + tag.BinarySize > ms.Length) return wires;
                    var b = r.ReadBytes(tag.BinarySize);
                    if (tag.Name == "Locations" && b.Length >= 24)
                        locs.Add(new Num.Vector3((float)BitConverter.ToDouble(b, 0),
                                                 (float)BitConverter.ToDouble(b, 8),
                                                 (float)BitConverter.ToDouble(b, 16)));
                }
                if (locs.Count >= 2) wires.Add((locs[0], locs[1]));
            }
            return wires;
        }

        // 3 本の統合ラインメッシュ（ベルト/パイプ/電力）を構築・追加する。ヒットテスト対象外＝箱のピック序数を壊さない。
        private void BuildConnections(List<SaveEntity>[] entByCat)
        {
            var belt = new LineBuilder();   // cat1 色
            var pipe = new LineBuilder();   // cat3 色
            var power = new LineBuilder();  // cat2 色
            int beltN = 0, pipeN = 0, powerN = 0;
            int beltSkip = 0, pipeSkip = 0, powerSkip = 0;

            // ベルト(1)・パイプ(3): ローカルスプライン → アクター回転+平行移動 → ヘリックス。
            foreach (var cat in new[] { 1, 3 })
            {
                var dst = cat == 1 ? belt : pipe;
                foreach (var e in entByCat[cat])
                {
                    PropertiesListV2 pl;
                    try { pl = FrameProps(e); } catch { pl = null; }
                    var sp = pl?.Properties.FirstOrDefault(p => p.Tag.Name == "mSplineData");
                    if (sp?.Body == null) { if (cat == 1) beltSkip++; else pipeSkip++; continue; }
                    List<Num.Vector3> local;
                    try { local = DecodeSplineLocal(sp.Body); } catch { continue; }
                    if (local.Count < 2) continue;

                    var rr = e.Rotation;
                    var q = Num.Quaternion.Identity;
                    if (rr != null)
                    {
                        var cand = new Num.Quaternion(rr.X, rr.Y, rr.Z, rr.W);
                        if (cand.LengthSquared() > 1e-6f) q = Num.Quaternion.Normalize(cand);
                    }
                    var tp = e.Position;
                    var t = tp != null ? new Num.Vector3(tp.X, tp.Y, tp.Z) : Num.Vector3.Zero;

                    Num.Vector3 prev = default; bool have = false;
                    foreach (var lp in local)
                    {
                        var w = Num.Vector3.Transform(lp, q) + t;   // ローカル→ワールド cm（ゲーム軸）
                        var h = ToHelix(w.X, w.Y, w.Z);
                        if (have) { dst.AddLine(prev, h); if (cat == 1) beltN++; else pipeN++; }
                        prev = h; have = true;
                    }
                }
            }

            // 電力(2): mWireInstances は絶対ワールド端点。アクター変換不要。PowerLine_C のみ線を持つ。
            foreach (var e in entByCat[2])
            {
                if (!(e.TypePath ?? "").EndsWith("PowerLine_C")) continue; // ポール/タワーは線を持たない
                PropertiesListV2 pl;
                try { pl = FrameProps(e); } catch { pl = null; }
                var wi = pl?.Properties.FirstOrDefault(p => p.Tag.Name == "mWireInstances");
                if (wi?.Body == null) { powerSkip++; continue; }
                List<(Num.Vector3 a, Num.Vector3 b)> ws;
                try { ws = DecodeWires(wi.Body); } catch { continue; }
                foreach (var (a, b) in ws)
                {
                    power.AddLine(ToHelix(a.X, a.Y, a.Z), ToHelix(b.X, b.Y, b.Z));
                    powerN++;
                }
            }

            void Add(LineBuilder lb, int catColor, int segs)
            {
                if (segs == 0) return;
                viewport.Items.Add(new LineGeometryModel3D
                {
                    Geometry = lb.ToLineGeometry3D(),
                    Color = Pal[catColor],
                    Thickness = 1.4,
                    FixedSize = false,
                    IsThrowingShadow = false,
                    IsHitTestVisible = false, // ピックは箱モデルのみ（序数不変条件を壊さない）
                });
            }
            Add(belt, 1, beltN); Add(pipe, 3, pipeN); Add(power, 2, powerN);
            log.Info($"3D connections: belt={beltN}(skip {beltSkip}) pipe={pipeN}(skip {pipeSkip}) power={powerN}(skip {powerSkip}) segments");
        }

        // BuildScene と同じセグメント抽出（/ と . の末尾）。クローンのカテゴリ色算出に使う。
        private static string LastSeg(string typePath)
        {
            var seg = typePath ?? "";
            var slash = seg.LastIndexOf('/'); if (slash >= 0) seg = seg.Substring(slash + 1);
            var dot = seg.LastIndexOf('.'); if (dot >= 0) seg = seg.Substring(dot + 1);
            return seg;
        }
    }
}
