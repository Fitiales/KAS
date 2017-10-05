﻿// Kerbal Attachment System
// Mod's author: KospY (http://forum.kerbalspaceprogram.com/index.php?/profile/33868-kospy/)
// Module author: igor.zavoychinskiy@gmail.com
// License: Public Domain

using KSPDev.GUIUtils;
using KSPDev.PartUtils;
using KSPDev.ProcessingUtils;
using KASAPIv1;
using System;
using System.Linq;
using UnityEngine;

namespace KAS {

/// <summary>Module that allows connecting the parts by a mouse via GUI.</summary>
/// <remarks>
/// When the player starts the linking mode, he must either complete it by clicking on a compatible
/// target part or abort the mode altogether.
/// <para>
/// EVA kerbal movement is locked when linking mode is active, so both source and target parts
/// must be in the range from the kerbal.
/// </para>
/// </remarks>
// Next localization ID: #kasLOC_01002.
public sealed class KASModuleInteractiveLinkSource : KASModuleLinkSourceBase,
    // KSPDev interfaces.
    IHasContextMenu {

  #region Localizable GUI strings
  /// <include file="SpecialDocTags.xml" path="Tags/Message1/*"/>
  /// <include file="KSPDevUtilsAPI_HelpIndex.xml" path="//item[@name='T:KSPDev.GUIUtils.DistanceType']/*"/>
  static readonly Message<DistanceType> CanBeConnectedMsg = new Message<DistanceType>(
      "#kasLOC_01000",
      defaultTemplate: "Click to establish a link (length <<1>>)",
      description: "Message to display when a compatible target part is hovered over and the source"
      + " is in the linking mode."
      + "\nArgument <<1>> is the possible link length of type DistanceType.",
      example: "Click to establish a link (length 1.22 m)");

  /// <include file="SpecialDocTags.xml" path="Tags/Message0/*"/>
  static readonly Message LinkingInProgressMsg = new Message(
      "#kasLOC_01001",
      defaultTemplate: "Select a compatible socket or press ESC",
      description: "Message to display as a help string when an interactive linking mode has"
      + " started.");
  #endregion

  #region Local members
  /// <summary>Color of pipe in the linking mode when link can be established.</summary>
  static readonly Color GoodLinkColor = new Color(0, 1, 0, 0.5f);
  /// <summary>Color of pipe in the linking mode when link cannot be established.</summary>
  static readonly Color BadLinkColor = new Color(1, 0, 0, 0.5f);
  /// <summary>A lock that restricts anything but camera positioning.</summary>
  const string TotalControlLock = "KASInteractiveJointUberLock";
  /// <summary>Shader that reders pipe during linking.</summary>
  const string InteractiveShaderName = "Transparent/Diffuse";  
  /// <summary>Compativle target under mouse cursor.</summary>
  ILinkTarget targetCandidate;
  /// <summary>Tells if connection with the candidate will be sucessfull.</summary>
  bool targetCandidateIsGood;
  /// <summary>Last known hovered part. Used to trigger detection of the target candidate.</summary>
  Part lastHoveredPart;
  /// <summary>Displayed during interactive linking.</summary>
  ScreenMessage statusScreenMessage;
  #endregion

  #region Event names. Keep them in sync with the event names!
  /// <summary>Name of the relevant event. It must match name of the method.</summary>
  /// <seealso cref="StartLinkContextMenuAction"/>
  const string StartLinkMenuActionName = "StartLinkContextMenuAction";  
  /// <summary>Name of the relevant event. It must match name of the method.</summary>
  /// <seealso cref="BreakLinkContextMenuAction"/>
  const string BreakLinkMenuActionName = "BreakLinkContextMenuAction";
  #endregion

  #region Part's config fields
  /// <summary>Audio sample to play when parts are docked by the player.</summary>
  /// <include file="SpecialDocTags.xml" path="Tags/ConfigSetting/*"/>
  [KSPField]
  public string plugSndPath = "KAS/Sounds/plugdocked";

  /// <summary>Audio sample to play when the parts are undocked by the player.</summary>
  /// <include file="SpecialDocTags.xml" path="Tags/ConfigSetting/*"/>
  [KSPField]
  public string unplugSndPath = "KAS/Sounds/unplugdocked";

  /// <summary>Name of the menu item to start linking mode.</summary>
  /// <include file="SpecialDocTags.xml" path="Tags/ConfigSetting/*"/>
  [KSPField]
  public string startLinkMenu = "Start a link";

  /// <summary>Name of the menu item to break currently established link.</summary>
  /// <include file="SpecialDocTags.xml" path="Tags/ConfigSetting/*"/>
  [KSPField]
  public string breakLinkMenu = "Break the link";
  #endregion

  #region PartModule overrides
  /// <inheritdoc/>
  public override void OnUpdate() {
    base.OnUpdate();
    if (linkState == LinkState.Linking && guiLinkMode == GUILinkMode.Interactive) {
      UpdateLinkingState();

      // Handle link mode cancel.
      if (Input.GetKeyUp(KeyCode.Escape)) {
        AsyncCall.CallOnEndOfFrame(this, () => CancelLinking(LinkActorType.Player));
      }
      // Handle link action (mouse click).
      if (targetCandidateIsGood && Input.GetKeyDown(KeyCode.Mouse0)) {
        AsyncCall.CallOnEndOfFrame(this, () => LinkToTarget(targetCandidate));
      }
    }
  }

  /// <inheritdoc/>
  public override void OnStart(PartModule.StartState state) {
    base.OnStart(state);
    // Infinity duration doesn't mean the message will be shown forever. It must be refreshed in the
    // Update method.
    statusScreenMessage = new ScreenMessage("", Mathf.Infinity, ScreenMessageStyle.UPPER_CENTER);
    UpdateContextMenu();
  }
  #endregion

  /// <summary>Variable to store auto save state before starting interactive mode.</summary>
  bool canAutoSaveState;
  #region IHasContextMenu implementation
  /// <inheritdoc/>
  public void UpdateContextMenu() {
    PartModuleUtils.SetupEvent(this, StartLinkContextMenuAction, e => {
                                 e.guiName = startLinkMenu;
                                 e.active = linkState == LinkState.Available;
                               });
    PartModuleUtils.SetupEvent(this, BreakLinkContextMenuAction, e => {
                                 e.guiName = breakLinkMenu;
                                 e.active = linkState == LinkState.Linked;
                               });
    PartModuleUtils.SetupEvent(this, DockVesselsContextMenuAction,
                               e => e.active = isLinked && !linkJoint.isDockOnLink);
    PartModuleUtils.SetupEvent(this, UndockVesselsContextMenuAction,
                               e => e.active = isLinked && linkJoint.isDockOnLink);
  }
  #endregion

  #region KASModuleLinkSourceBase overrides
  /// <inheritdoc/>
  public override bool StartLinking(GUILinkMode mode, LinkActorType actor) {
    // Don't allow EVA linking mode.
    if (mode != GUILinkMode.Interactive && mode != GUILinkMode.API) {
      return false;
    }
    return base.StartLinking(mode, actor);
  }

  /// <inheritdoc/>
  protected override void StartLinkGUIMode(GUILinkMode mode, LinkActorType actor) {
    base.StartLinkGUIMode(mode, actor);
    InputLockManager.SetControlLock(
        ControlTypes.All & ~ControlTypes.CAMERACONTROLS, TotalControlLock);
    canAutoSaveState = HighLogic.CurrentGame.Parameters.Flight.CanAutoSave;
    HighLogic.CurrentGame.Parameters.Flight.CanAutoSave = false;
    linkRenderer.shaderNameOverride = InteractiveShaderName;
    linkRenderer.colorOverride = BadLinkColor;
    linkRenderer.isPhysicalCollider = false;
  }

  /// <inheritdoc/>
  protected override void StopLinkGUIMode() {
    linkRenderer.StopRenderer();
    linkRenderer.shaderNameOverride = null;
    linkRenderer.colorOverride = null;
    linkRenderer.isPhysicalCollider = true;
    ScreenMessages.RemoveMessage(statusScreenMessage);
    InputLockManager.RemoveControlLock(TotalControlLock);
    HighLogic.CurrentGame.Parameters.Flight.CanAutoSave = canAutoSaveState;
    lastHoveredPart = null;
    base.StopLinkGUIMode();

    // Start renderer if link has been established.
    if (linkState == LinkState.Linked) {
      linkRenderer.StartRenderer(nodeTransform, linkTarget.nodeTransform);
    }
  }

  /// <inheritdoc/>
  protected override void OnStateChange(LinkState? oldState) {
    base.OnStateChange(oldState);
    UpdateContextMenu();
  }

  /// <inheritdoc/>
  public override void OnKASLinkCreatedEvent(KASEvents.LinkEvent info) {
    base.OnKASLinkCreatedEvent(info);
    if (info.actor == LinkActorType.Player || info.actor == LinkActorType.Physics) {
      UISoundPlayer.instance.Play(plugSndPath);
    }
  }

  /// <inheritdoc/>
  public override void OnKASLinkBrokenEvent(KASEvents.LinkEvent info) {
    base.OnKASLinkBrokenEvent(info);
    if (info.actor == LinkActorType.Player) {
      UISoundPlayer.instance.Play(unplugSndPath);
    } else if (info.actor == LinkActorType.Physics) {
      UISoundPlayer.instance.Play(CommonConfig.sndPathBipWrong);
    }
  }
  #endregion

  // TODO(ihsoft): Disallow non-eva control.
  #region Action handlers
  /// <summary>Event handler. Initiates a link that must be completed by a mouse click.</summary>
  [KSPEvent(guiName = "Start a link", guiActive = true, guiActiveUnfocused = true)]
  public void StartLinkContextMenuAction() {
    StartLinking(GUILinkMode.Interactive, LinkActorType.Player);
  }

  /// <summary>Event handler. Breaks current link between source and target.</summary>
  [KSPEvent(guiName = "Break the link", guiActive = true, guiActiveUnfocused = true)]
  public void BreakLinkContextMenuAction() {
    BreakCurrentLink(LinkActorType.Player);
  }
  #endregion

  #region Local utilities
  /// <summary>Displays linking status in real time.</summary>
  void UpdateLinkingState() {
    // Catch the hovered part, a possible target on it, and the link feasibility.
    if (Mouse.HoveredPart != lastHoveredPart) {
      lastHoveredPart = Mouse.HoveredPart;
      targetCandidateIsGood = false;
      if (lastHoveredPart == null ) {
        targetCandidate = null;
      } else {
        targetCandidate = lastHoveredPart.FindModulesImplementing<ILinkTarget>()
            .FirstOrDefault(x => x.cfgLinkType == cfgLinkType
                            && x.linkState == LinkState.AcceptingLinks);
        if (targetCandidate != null) {
          var linkStatusErrors = new[] {
              CheckBasicLinkConditions(targetCandidate),
              linkRenderer.CheckColliderHits(nodeTransform, targetCandidate.nodeTransform)
          };
          linkStatusErrors = linkStatusErrors
              .Concat(linkJoint.CheckConstraints(this, targetCandidate.nodeTransform))
              .Where(x => x != null).ToArray();
          if (linkStatusErrors.Length == 0) {
            targetCandidateIsGood = true;
            statusScreenMessage.message = CanBeConnectedMsg.Format(
                Vector3.Distance(nodeTransform.position, targetCandidate.nodeTransform.position));
          } else {
            statusScreenMessage.message = ScreenMessaging.SetColorToRichText(
                String.Join("\n", linkStatusErrors), ScreenMessaging.ErrorColor);
          }
        }
      }
      // Show the possible link or indicate the error.
      if (targetCandidate != null) {
        linkRenderer.colorOverride = targetCandidateIsGood ? GoodLinkColor : BadLinkColor;
        linkRenderer.StartRenderer(nodeTransform, targetCandidate.nodeTransform);
      } else {
        linkRenderer.colorOverride = BadLinkColor;
        linkRenderer.StopRenderer();
      }
    }

    // Update linking messages (it needs to be refreshed to not go out by timeout).
    if (targetCandidate == null) {
      statusScreenMessage.message = LinkingInProgressMsg;
    }
    ScreenMessages.PostScreenMessage(statusScreenMessage);
  }
  #endregion
}

}  // namespace
