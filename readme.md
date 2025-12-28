# BugViewer
[![Nuget](https://img.shields.io/nuget/v/BugViewer?style=plastic)](https://www.nuget.org/packages/BugViewer/)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/micampbell/BugViewer)

![BugViewer](https://raw.githubusercontent.com/micampbell/BugViewer/refs/heads/master/docs/logo.png)
## Why Bug?
The most appropriate name would be Blazor WebGPU Viewer. This could be abbreviated as BWG Viewer, which is fine - but awkward to say and type. So, we simply change the double-U to a single-U -> thus BugViewer.
## Description
BugViewer is a Blazor component that renders a 3D file with the new WebGPU. It includes controls to view a single part (as opposed to being a game engine). The goal is to provide a clear method to view 3D parts with minimal Javascript.

The main things that are visualized are:
- triangles/meshes
- polylines
- text billboards

## Installation
BugViewer can be installed as Nuget package: https://www.nuget.org/packages/BugViewer/

## Usage

### Simple scenario

Just add the `BugViewer` Component to your Razor page.:

```csharp
        <BugViewer @ref="viewer" Height="80%" Width="80%" />

```

The [./samples](./samples) folder contains some examples of how to setup the canvas.
