﻿// Kerbal Attachment System
// Mod's author: KospY (http://forum.kerbalspaceprogram.com/index.php?/profile/33868-kospy/)
// Module author: igor.zavoychinskiy@gmail.com
// License: https://github.com/KospY/KAS/blob/master/LICENSE.md

using System;
using System.Linq;
using TestScripts;
using UnityEngine;
using KASAPIv1;
using HighlightingSystem;

namespace KAS {

public class KASModuleTelescopicTubeRenderer : KASModuleTubeRenderer {
  #region ILinkTubeRenderer config propertiers implementation
  #endregion

  // These fileds must not be accessed outside of the module. They are declared public only
  // because KSP won't work otherwise. Ancenstors and external callers must access values via
  // interface properties. If property is not there then it means it's *intentionally* restricted
  // for the non-internal consumers.
  #region Part's config fields
  [KSPField]
  public float pistonLength = 0.2f;
  [KSPField]
  public int pistonsCount = 3;
  [KSPField]
  public float pistonWallThickness = 0.01f;
  #endregion

  /// <inheritdoc/>
  public override Color? colorOverride {
    set {
      base.colorOverride = value;
      if (pistons != null) {
        pistons.ToList().ForEach(x => SetColor(x, value ?? color));
      }
    }
  }
  /// <inheritdoc/>
  public override string shaderNameOverride {
    set {
      base.shaderNameOverride = value;
      if (isStarted && pistons != null) {
        var newMaterial = CreateMaterial();
        pistons.ToList().ForEach(x => UpdateMaterial(x, newMaterial));
      }
    }
  }

  protected GameObject[] pistons;

  protected override void CreateModelMeshes() {
    base.CreateModelMeshes();
//    linkPipeMR.enabled = false;
//    CreatePistons(parent: pipeBeginTransform);
    Debug.LogWarningFormat("*** LOADING PART: create meshes telescopic {0}", part.name);
  }

  #region ILinkTubeRenderer implementation
  /// <inheritdoc/>
  public override void StartRenderer(Transform source, Transform target) {
    Debug.LogWarningFormat("******** UNEXPECTED! {0}", part.name);
    base.StartRenderer(source, target);
    linkPipeMR.enabled = false;
    CreatePistons();
  }

  void CreatePistons(Transform parent = null) {
    DeletePistons();
    pistons = new GameObject[pistonsCount];
    var startDiameter = pipeDiameter;
    for (var i = 0; i < pistonsCount; ++i) {
      var piston = CreatePrimitive(PrimitiveType.Cylinder, startDiameter, parent: parent);
      piston.name = "piston" + i;
      piston.transform.localScale = new Vector3(startDiameter, startDiameter, pistonLength);
      startDiameter -= 2 * pistonWallThickness;
      RescaleTextureToLength(piston);
      pistons[i] = piston;
    }
    //FIXME: let base method doing it
    part.HighlightRenderers = null;  // Force refreshing the model.
    UpdatePistons();
  }

  void SetupCylinderPrimitive(GameObject obj, Vector3 pos, Vector3 dir) {
    //FIXME: work in local source transfrom space
//    obj.transform.position = pos + dir * (obj.transform.localScale.z / 2);
//    obj.transform.LookAt(obj.transform.position + dir);
  }

  void UpdatePistons() {
    var fromPos = sourceJointNode.position;
    var toPos = targetJointNode.position;
    var link = toPos - fromPos;
    var linkLength = link.magnitude;
    // First piston has fixed position due to it's attached to the source.
    pistons[0].transform.localPosition = new Vector3(0, 0, pistonLength / 2);
    // Last piston has fixed position due to it's atatched to the target.
    pistons[pistons.Length - 1].transform.localPosition =
        new Vector3(0, 0, linkLength - pistonLength / 2);
    // Pistions between first and last monotonically fill the link.
    if (pistons.Length > 2) {
      // FIXME: back off for the sphere radius
      // FIXME: OR virtually extend last piston by the sphere radius.
      // FIXME: AND may be do the same with the first piston      
      var linkStep = (linkLength - sphereDiameter / 2 - pistonLength) / (pistonsCount - 2);
      for (var i = 1; i < pistons.Length - 1; ++i) {
        pistons[0].transform.localPosition = new Vector3(0, 0, pistonLength / 2 + i * linkStep);
      }
    }
  }

  void DeletePistons() {
    if (pistons != null) {
      foreach (var piston in pistons) {
        piston.DestroyGameObject();
      }
      pistons = null;
    }
  }

  /// <inheritdoc/>
  public override void StopRenderer() {
    base.StopRenderer();
    DeletePistons();
  }

  /// <inheritdoc/>
  public override void UpdateLink() {
    base.UpdateLink();
    if (pistons != null) {
      UpdatePistons();
    }
  }
  #endregion
}

}  // namespace