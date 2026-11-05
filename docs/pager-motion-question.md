# 帮我设计一个 WPF 分页指示器（页码）的动效与实现

## 背景

我在做一个 Windows 桌面整理工具 **Desktop Drawer**：它把桌面图标做成一个全屏、分页的网格，盖在壁纸层（透明顶层窗口，永远在所有应用窗口之下、桌面之上）。技术栈 **.NET 9 + WPF**，不引入任何第三方库。

- 桌面图标被分成 **8 页**（页数会随图标数量变化，通常 3–12 页）
- 翻页方式：鼠标滚轮、触摸板双指横滑、`PageUp/PageDown`、`Alt+←/→`、点击底部页码、点击左右箭头
- 翻页时内容区整体横向滑动：**旧页加速滑出（CubicEase EaseIn），新页减速滑入（EaseOut），300 ms**，两页同时进行
- 底部有一个"胶囊"分页条，浮在桌面右上/下方，半透明深色（`#A60B0E16`）、圆角 18、细描边

胶囊内结构（从左到右）：

```
‹  Folders   1  2  3  4  5  6  7  8  ›
```

- `‹` / `›`：上一页 / 下一页按钮（第一页/最后一页时禁用）
- `Folders`：当前分组名（纯文字，`TextMutedBrush`）
- `1..8`：页码，每个是 26×26 的按钮；**当前页是一个填满主题色的圆形**，其余是纯文字
- 文字底色透明，鼠标悬停时出现淡白色圆角高亮

## 现有实现（关键代码）

页码模型：

```csharp
public sealed class PageDot
{
    public required int Index { get; init; }
    public required string Label { get; init; }   // "1", "2", ...
    public required bool IsCurrent { get; init; }
    public required string Tooltip { get; init; }
}
```

XAML（简化）：

```xml
<Border x:Name="PagerBar" Background="#A60B0E16" BorderBrush="#3DFFFFFF"
        BorderThickness="1" CornerRadius="18" Padding="9,3">
  <StackPanel Orientation="Horizontal">
    <Button x:Name="PrevButton" Style="{StaticResource IconButton}" Width="28" Height="28" ... />
    <TextBlock Text="{Binding CurrentPageLabel}" FontSize="10.5" Margin="9,0" ... />
    <ItemsControl ItemsSource="{Binding PageDots}">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <Button Style="{StaticResource PageDotButton}" Content="{Binding Label}"
                  Tag="{Binding Index}" Click="PageDot_Click" />
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <Button x:Name="NextButton" Style="{StaticResource IconButton}" Width="28" Height="28" ... />
  </StackPanel>
</Border>
```

页码按钮样式（简化）：

```xml
<Style x:Key="PageDotButton" TargetType="Button">
  <Setter Property="Width" Value="26" />
  <Setter Property="Height" Value="26" />
  <Setter Property="Margin" Value="1.5,0" />
  <Setter Property="FontSize" Value="10.5" />
  <Setter Property="FontWeight" Value="SemiBold" />
  <Setter Property="Foreground" Value="{StaticResource TextMutedBrush}" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="Button">
        <Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="13">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Root" Property="Background" Value="#16FFFFFF" />
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
  <Style.Triggers>
    <DataTrigger Binding="{Binding IsCurrent}" Value="True">
      <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
      <Setter Property="Foreground" Value="#0B1020" />
    </DataTrigger>
  </Style.Triggers>
</Style>
```

翻页动画（内容区，已经实现）：

```csharp
TimeSpan duration = TimeSpan.FromMilliseconds(300);
var incomingEase = new CubicEase { EasingMode = EasingMode.EaseOut };
var outgoingEase = new CubicEase { EasingMode = EasingMode.EaseIn };

// 旧页（一张快照 Image）从 0 滑到 -direction*width
PageSnapshotImage.RenderTransform = snapshotTransform;
// 新页（真正的列表）从 direction*width 滑到 0
pageTransform.BeginAnimation(TranslateTransform.XProperty,
    new DoubleAnimation(direction * width, 0, duration) { EasingFunction = incomingEase });
```

## 问题

1. **页码之间"连接很差"**：当前页高亮是"瞬间跳过去"的（`DataTrigger` 直接换背景色），和内容区 300 ms 的整页滑动完全脱节；连续快速翻页时尤其明显，感觉页码和内容是两套东西。
2. 页码数字的字重/颜色也是瞬间切换，没有过渡。
3. 点击远处的页码（例如从第 1 页直接跳到第 7 页）时，内容照常滑动，但页码没有任何"中间过程"。
4. 页数变化（3 页 ↔ 12 页）时胶囊宽度会跳一下。

## 我希望你给什么

### 1) 动效方案（先方案，后推荐）

给我 2–3 个具体的视觉方案，例如：

- 一个**单独的"高亮胶囊"层**在数字下面滑动（当前页的数字始终居中于胶囊）
- 一条移动的**下划线/进度条**
- 数字本身放大 + 颜色过渡
- 或者你认为更适合"桌面图标分页"的其他方案

每个方案请说明：观感、实现复杂度、在 3–12 页时的表现、和 300 ms 内容滑动的配合。

### 2) 参数

- 高亮滑动的时长、缓动，是否要和内容滑动完全一致（300 ms EaseInOut？还是更短更跟手？）
- 前进 / 后退是否对称
- 相邻翻页 vs 跨页跳转（1 → 7）是否用不同时长
- 连续快速翻页时如何打断/接续动画

### 3) WPF 实现（可直接落地）

要求：

- 纯 WPF，无第三方库、无 WinUI
- **不能有模糊、阴影、发光**（产品设计禁止）
- 高亮层要在数字**下面平滑移动**，不要每个按钮各自淡入淡出
- 动画期间**不能触发布局**（只动 RenderTransform / Canvas.Left 之类）
- 保持 60 fps
- 现有结构是 `ItemsControl + StackPanel`，每项是 26×26、间距 1.5
- 需要支持：鼠标悬停、点击、禁用态（第一页/最后一页箭头）、页数动态变化

请给出关键 XAML 和 C# 代码骨架，包括：

- 如何拿到某个页码按钮相对容器的位置（`TransformToAncestor` / 布局后的坐标）
- 如何用一个 `Border` 作为滑动高亮并在页数变化时重新定位
- 如何在 `GoToPage` 里同步触发高亮动画（同时处理"动画中再次翻页"）

### 4) 边界与坑

- 动画进行中再次翻页
- 首/末页、只有 1 页时
- 页数变化导致按钮重新生成
- 高 DPI（200%）下的坐标取整与 1px 抖动
- 触摸板快速连续滑动（每 0.5 s 一次翻页）

## 输出格式

1. 先列 2–3 个动效方案 + 推荐
2. 再给推荐方案的完整 WPF 实现（XAML + C#）
3. 最后列常见坑与规避方法
