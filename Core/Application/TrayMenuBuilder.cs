using System;
using System.Drawing;
using System.Windows.Forms;

namespace gtt_sidebar.Core.Application
{
    /// <summary>
    /// 托盘右键菜单构建器：把 WinForms 默认灰色菜单调整为与 v2.0 设计系统一致的观感。
    ///
    /// 能力范围（ContextMenuStrip 原生限制内）：
    /// - 白色底、细边框、12px 圆角（圆角为 Win10/11 系统级视觉，此处靠白底+细边贴近设计）
    /// - 菜单项配色/悬停高亮/勾选标记统一用设计令牌色值
    /// - 顶部加入应用名标头（禁用态呈现，灰字小号）
    ///
    /// 为什么不用 WPF 重写整个菜单：NotifyIcon 只接受 WinForms ContextMenuStrip，
    /// 自绘 WPF 弹窗要做激活/失焦/屏幕边缘定位等一堆边角处理，收益低。
    /// </summary>
    internal static class TrayMenuBuilder
    {
        // 与 DesignSystem.xaml 令牌保持一致的色值（WinForms 无 DynamicResource，只能抄值）
        private static readonly Color TextPrimary   = Color.FromArgb(0x1A, 0x1D, 0x23);
        private static readonly Color TextSecondary = Color.FromArgb(0x5A, 0x62, 0x70);
        private static readonly Color TextTertiary  = Color.FromArgb(0x90, 0x99, 0xA8);
        private static readonly Color SurfaceCard   = Color.White;
        private static readonly Color SurfaceHover  = Color.FromArgb(0xEE, 0xF1, 0xF5);
        private static readonly Color BorderSubtle  = Color.FromArgb(0xE8, 0xEB, 0xF0);
        private static readonly Color AccentPressed = Color.FromArgb(0x3A, 0x7B, 0xC8);
        private static readonly Color AccentSoft    = Color.FromArgb(0xEA, 0xF2, 0xFC);
        private static readonly Color StateDanger   = Color.FromArgb(0xFF, 0x4D, 0x4F);

        private static readonly Font ItemFont  = new Font("Microsoft YaHei UI", 9f);
        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Regular);

        /// <summary>
        /// 应用整体视觉（白底、配色、圆角提示、渲染质量）
        /// </summary>
        public static ContextMenuStrip CreateMenu()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = SurfaceCard,
                ForeColor = TextPrimary,
                ShowImageMargin = true,     // 保留图像边距：勾选标记需要展示位
                ShowCheckMargin = false,    // 勾选画在图像位，不另开一列
                Font = ItemFont,
                Renderer = new DesignRenderer(),
                AutoSize = true,
            };

            return menu;
        }

        /// <summary>添加普通菜单项（设计系统配色）</summary>
        public static ToolStripMenuItem AddItem(ContextMenuStrip menu, string text, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick)
            {
                ForeColor = TextPrimary,
                Font = ItemFont,
                Padding = new Padding(0, 5, 12, 5),
            };
            menu.Items.Add(item);
            return item;
        }

        /// <summary>添加带勾选状态的菜单项，返回菜单项便于后续更新勾选</summary>
        public static ToolStripMenuItem AddCheckItem(ContextMenuStrip menu, string text, bool isChecked, EventHandler onClick)
        {
            var item = AddItem(menu, text, onClick);
            item.Checked = isChecked;
            return item;
        }

        /// <summary>添加分隔线（细浅灰，替换系统默认粗灰线）</summary>
        public static void AddSeparator(ContextMenuStrip menu)
        {
            menu.Items.Add(new ToolStripSeparator
            {
                BackColor = SurfaceCard,
            });
        }

        /// <summary>添加窗口顶部应用名标头（禁用态、灰字，仅作识别）</summary>
        public static void AddHeader(ContextMenuStrip menu, string text)
        {
            var header = new ToolStripMenuItem(text)
            {
                Enabled = false,
                ForeColor = TextTertiary,
                Font = HeaderFont,
            };
            menu.Items.Add(header);
        }

        /// <summary>
        /// 设计系统渲染器：白底、细边框、浅灰悬停、蓝色按压、对勾用主色。
        /// </summary>
        private class DesignRenderer : ToolStripProfessionalRenderer
        {
            public DesignRenderer()
                : base(new DesignColorTable())
            {
                RoundedEdges = true;
            }

            /// <summary>
            /// ProfessionalColorTable 的 ImageMargin* 属性只读（内部走 UseSystemColors 逻辑），
            /// 必须子类重写才能返回自定义色；其余属性一并重写以统一配色。
            /// </summary>
            private class DesignColorTable : ProfessionalColorTable
            {
                // 悬停/按压高亮
                public override Color MenuItemSelected => SurfaceHover;
                public override Color MenuItemSelectedGradientBegin => SurfaceHover;
                public override Color MenuItemSelectedGradientEnd => SurfaceHover;
                public override Color MenuItemBorder => BorderSubtle;

                // 按压态（蓝色柔和底）
                public override Color MenuItemPressedGradientBegin => AccentSoft;
                public override Color MenuItemPressedGradientEnd => AccentSoft;

                // 分隔线与外框
                public override Color SeparatorDark => BorderSubtle;
                public override Color SeparatorLight => SurfaceCard;
                public override Color MenuBorder => BorderSubtle;
                public override Color ToolStripBorder => SurfaceCard;
                public override Color ToolStripDropDownBackground => SurfaceCard;

                // 图像边距列（勾选标记所在列）保持白底
                public override Color ImageMarginGradientBegin => SurfaceCard;
                public override Color ImageMarginGradientMiddle => SurfaceCard;
                public override Color ImageMarginGradientEnd => SurfaceCard;
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var rect = new Rectangle(Point.Empty, e.Item.Size);
                var g = e.Graphics;

                // 选中（悬停）态：浅灰圆角高亮；禁用项不上色
                if (e.Item.Selected && e.Item.Enabled)
                {
                    using (var b = new SolidBrush(SurfaceHover))
                    {
                        FillRounded(g, b, rect, 7);
                    }
                }
                else if (e.Item.Pressed)
                {
                    using (var b = new SolidBrush(AccentSoft))
                    {
                        FillRounded(g, b, rect, 7);
                    }
                }

                e.Item.ForeColor = e.Item.Enabled ? TextPrimary : TextTertiary;
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // 画主蓝色对勾，替代系统默认的深色勾
                var size = e.ImageRectangle;
                if (size.Width <= 0 || size.Height <= 0) return;

                using (var pen = new Pen(AccentPressed, 1.8f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    float cx = size.Left + size.Width / 2f;
                    float cy = size.Top + size.Height / 2f;
                    float s = Math.Min(size.Width, size.Height) * 0.28f;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - s, cy),
                        new PointF(cx - s * 0.3f, cy + s * 0.7f),
                        new PointF(cx + s, cy - s * 0.7f),
                    });
                }
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                // 细边框 + 白底，替换系统默认的 3D 凸起边
                var g = e.Graphics;
                var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using (var pen = new Pen(BorderSubtle))
                {
                    g.DrawRectangle(pen, rect);
                }
            }

            private static void FillRounded(Graphics g, Brush brush, Rectangle rect, int radius)
            {
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int d = radius * 2;
                    rect.Inflate(-1, -1);
                    path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
                    path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
                    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                    path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    g.FillPath(brush, path);
                }
            }
        }
    }
}
