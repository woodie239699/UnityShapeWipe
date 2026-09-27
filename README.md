\# Unity Shape Wipe



A Celeste-inspired shape-mask transition for Unity — arbitrary shapes, up to 200 instances, visual editor, runtime randomization.



![Demo](Documentation~/Demo.gif)



\## Features



\- Any shape (PNG alpha mask)

\- Up to 200 instances

\- 6 delay order modes (Row Major, Column Major, Diagonal, Row Pairs, Row Constant, Column Constant)

\- Visual editor with 16:9 preview

\- Runtime randomization (position / rotation / delay / end scale)

\- Easing (Linear / EaseIn / EaseOut / EaseInOut)

\- Reverse phases and flip modes

\- UI scale sync

\- Built-in render pipeline



\## Requirements



\- Unity 2018.4 or newer

\- Built-in Render Pipeline



\## Installation



\### Via Git URL



1\. Open Unity → Window → Package Manager

2\. Click `+` → Add package from git URL

3\. Paste: `https://github.com/woodie239699/UnityShapeWipe.git`



\### Manually



Copy the `Runtime/` and `Editor/` folders into your `Assets/` directory.



\## Quick Start



1\. Create a material using the `ShapeWipe/Mask` shader.

2\. Assign your shape PNG to the material's `\_MainTex`.

3\. Add a full-screen UI Image to your scene and assign the material.

4\. Add the `ShapeWipe` component to any GameObject.

5\. Assign the material and configure shapes in the Inspector.

6\. Call `StartCoroutine(wipe.Play())` when you want the transition.



See `Samples\~/Demo` for a working example.



\## Parameters



| Field | Description |

|---|---|

| `Close Duration` | Time for the shape to grow and cover the screen |

| `Open Duration` | Time for the shape to grow again and reveal |

| `Hold Duration` | Pause on full black |

| `Easing` | Linear / EaseIn / EaseOut / EaseInOut |

| `Reverse Close Phase` | Play the close phase backwards |

| `Reverse Open Phase` | Play the open phase backwards |

| `Close Flip` / `Open Flip` | Mirror the delay ramp (H/V/Both) |

| `Randomize Position` | Random offset per shape each Play() |

| `Randomize Rotation` | Random rotation per shape each Play() |

| `Randomize Delay` | Random extra delay per shape each Play() |

| `Randomize End Scale` | Random final size per shape each Play() |



\## Events



\- `On Screen Covered` — fires when the screen is fully black

\- `On Transition Done` — fires when the animation finishes



\## Advanced Usage



\### Wait for async load before opening



```csharp

StartCoroutine(wipe.Play(() =>

{

&#x20;   AsyncOperation op = SceneManager.LoadSceneAsync("Level2");

&#x20;   while (!op.isDone) yield return null;

}));

