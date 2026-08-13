using System;
using System.Collections.Generic;
using System.IO;
using Border.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 외부 패키지(Kevin Iglesias / Invector LITE)의 휴머노이드 클립을 프로젝트 공용 폴더로 추출하고,
/// 플레이어·좀비 애니메이터 컨트롤러에 스트레이프 블렌드 트리와 제자리 회전을 구성하는 1회용 에디터 툴.
///
/// FBX 서브에셋을 독립 .anim 으로 복제하는 이유는 두 가지다.
/// 1) 같은 휴머노이드 규격이라 한 폴더에서 같이 관리하는 편이 낫다.
/// 2) 복제 후에는 원본 패키지(데모 씬·스크립트·프리팹 포함)를 통째로 지울 수 있다.
/// 대신 복제 시점의 임포트 설정이 클립에 굳으므로, FBX 쪽 설정을 바꿔도 반영되지 않는다.
///
/// 회전은 이미 코드가 만든다(PlayerController.bodyYaw / ZombieStateMachine.UpdateFacing).
/// 그래서 Turn 은 포즈가 제자리인 [RM] 클립만 쓴다 — 루트 회전 커브는 어느 쪽에서도 적용되지 않아
/// 회전이 두 번 먹지 않고 "발만 끄는" 연출로 남는다.
/// </summary>
public static class AnimationSetupTool
{
    private const string destFolder = "Assets/04. Arts/Animations";
    private const string legsMaskPath = destFolder + "/Human Legs Mask.mask";

    private const string kiTurnFolder = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Movement/Turn";
    private const string invectorFolder = "Assets/Invector-3rdPersonController_LITE/3D Models/Animations";
    private const string zombieAnimFolder = "Assets/04. Arts/Animations/Zombie";

    private const string playerControllerPath = "Assets/04. Arts/Player/Animations/PlayerLocomotion.controller";
    private const string zombieControllerPath = "Assets/02. Prefab/Zombie/ZombieLocomotion.controller";

    private const string zombieTurnLayerName = "Turn (Legs)";

    // 파라미터 이름 — 스크립트(PlayerAnimator/ZombieAnimator)와 반드시 같아야 한다
    private const string paramSpeed = "Speed";
    private const string paramMoveX = "MoveX";
    private const string paramMoveY = "MoveY";
    private const string paramTurnBlend = "TurnBlend";
    private const string paramTurnMul = "TurnMul";
    private const string paramIsCrouching = "IsCrouching";
    private const string paramIsDisguised = "IsDisguised";

    // 플레이어 1D 속도 트리 문턱값(m/s). PlayerController.moveSpeed 3 / 웅크림 1.5 실측 기준
    private const float walkSpeed = 1.5f;
    private const float runSpeed = 3f;

    // 웅크림 이동속도(m/s) = PlayerController.moveSpeed(3) × crouchSpeedMultiplier(0.5). Run 계단이 없다 —
    // Mixamo 원본에 크라우치 런 클립이 없고, 게임 로직도 웅크린 채로는 뛸 수 없다.
    private const float crouchWalkSpeed = 1.5f;

    // Locomotion ↔ Turn 전이 히스테리시스. TurnBlend 는 정규화된 부호 있는 회전량(-1~1)이다
    private const float turnEnterBlend = 0.15f;
    private const float turnExitBlend = 0.08f;
    private const float turnEnterSpeed = 0.15f;

    /// <summary>추출할 클립 한 건의 정의. 원본 FBX 경로·내부 클립 이름·저장할 이름을 묶는다.</summary>
    private readonly struct ClipSource
    {
        public readonly string FbxPath;   // 원본 FBX 에셋 경로
        public readonly string ClipName;  // FBX 안의 서브에셋 클립 이름
        public readonly string DestName;  // 저장할 .anim 파일 이름(확장자 제외)

        public ClipSource(string fbxPath, string clipName, string destName)
        {
            FbxPath = fbxPath;
            ClipName = clipName;
            DestName = destName;
        }
    }

    /// <summary>추출 대상 12종. Turn 2 + 스트레이프 Walk 5 + 스트레이프 Run 5.</summary>
    private static readonly ClipSource[] clipSources =
    {
        new ClipSource(kiTurnFolder + "/HumanM@Turn01_Left [RM].fbx", "HumanM@Turn01_Left [RM]", "Turn_Left"),
        new ClipSource(kiTurnFolder + "/HumanM@Turn01_Right [RM].fbx", "HumanM@Turn01_Right [RM]", "Turn_Right"),

        new ClipSource(invectorFolder + "/Basic_FreeMovement.fbx", "Walk", "Strafe_WalkForward"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "WalkBack", "Strafe_WalkBack"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "WalkForwardRight", "Strafe_WalkForwardRight"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "WalkBackRight", "Strafe_WalkBackRight"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "WalkRight", "Strafe_WalkRight"),

        new ClipSource(invectorFolder + "/Basic_FreeMovement.fbx", "Run", "Strafe_RunForward"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "RunBack", "Strafe_RunBack"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "RunForwardRight", "Strafe_RunForwardRight"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "RunBackRight", "Strafe_RunBackRight"),
        new ClipSource(invectorFolder + "/Basic_StrafeMoveset.fbx", "RunRight", "Strafe_RunRight"),
    };

    // Mixamo(mixamo.com) 에서 "FBX for Unity, Without Skin" 으로 받은 크라우치 클립 2종.
    // 서브에셋 클립 이름이 둘 다 "mixamo.com" 으로 동일하다(Mixamo 기본 테이크 이름).
    private static readonly ClipSource[] crouchClipSources =
    {
        new ClipSource(destFolder + "/Crouching Idle.fbx", "mixamo.com", "Crouch_Idle"),
        new ClipSource(destFolder + "/Crouch Walk.fbx", "mixamo.com", "Crouch_Walk"),
    };

    // 위장 걸음 — 좀비 자체 걷기 클립을 재사용한다. 회전은 이미 코드가 만들므로(HandleBodyTurn)
    // [RM](루트 모션 제거) 변형을 쓴다 — 그러지 않으면 클립 내장 이동이 코드 이동과 겹쳐 미끄러진다.
    private static readonly ClipSource[] disguiseClipSources =
    {
        new ClipSource(zombieAnimFolder + "/Zombie@Walk01.fbx", "Zombie@Walk01 [RM]", "Disguise_Walk"),
    };

    /// <summary>FBX 서브에셋 클립을 공용 폴더로 독립 .anim 복제한다. 이미 있으면 GUID 를 지키며 내용만 덮는다.</summary>
    [MenuItem("Tools/Animation/1. 클립 추출 (Turn + Strafe)")]
    public static void ExtractClips()
    {
        Log.D("[AnimationSetup] 클립 추출 시작");
        EnsureFolder(destFolder);

        int done = 0;
        foreach (ClipSource source in clipSources)
        {
            AnimationClip src = FindClipInFbx(source.FbxPath, source.ClipName);
            if (src == null)
            {
                Debug.LogError($"[AnimationSetup] 클립을 찾지 못함: {source.FbxPath} :: {source.ClipName}");
                continue;
            }

            string destPath = $"{destFolder}/{source.DestName}.anim";
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(src);
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);

            if (existing != null)
            {
                // CreateAsset 으로 덮으면 GUID 가 바뀌어 컨트롤러 참조가 끊긴다. 내용만 복사한다.
                EditorUtility.CopySerialized(src, existing);
                existing.name = source.DestName;
                AnimationUtility.SetAnimationClipSettings(existing, settings);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AnimationClip copy = UnityEngine.Object.Instantiate(src);
                copy.name = source.DestName;
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                AssetDatabase.CreateAsset(copy, destPath);
            }

            done++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log.D($"[AnimationSetup] 클립 추출 완료: {done}/{clipSources.Length}");
    }

    /// <summary>좀비 Turn 레이어용 다리 전용 휴머노이드 아바타 마스크를 만든다. Root·Body·상체는 모두 끈다.</summary>
    [MenuItem("Tools/Animation/2. 다리 아바타 마스크 생성")]
    public static void CreateLegsMask()
    {
        Log.D("[AnimationSetup] 다리 마스크 생성 시작");
        EnsureFolder(destFolder);

        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(legsMaskPath);
        bool isNew = mask == null;
        if (isNew) mask = new AvatarMask();

        foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;

            bool active = part == AvatarMaskBodyPart.LeftLeg
                       || part == AvatarMaskBodyPart.RightLeg
                       || part == AvatarMaskBodyPart.LeftFootIK
                       || part == AvatarMaskBodyPart.RightFootIK;
            mask.SetHumanoidBodyPartActive(part, active);
        }

        if (isNew) AssetDatabase.CreateAsset(mask, legsMaskPath);
        else EditorUtility.SetDirty(mask);

        AssetDatabase.SaveAssets();
        Log.D($"[AnimationSetup] 다리 마스크 {(isNew ? "생성" : "갱신")}: {legsMaskPath}");
    }

    /// <summary>
    /// 플레이어 컨트롤러의 Locomotion 을 1D(Speed) → 2D 스트레이프 트리로 재구성하고, 제자리 회전 상태를 붙인다.
    /// Turn 을 별도 레이어가 아니라 Base Layer 상태로 두는 이유는 OwnerNetworkAnimator 가 파라미터만 복제하기 때문이다 —
    /// 레이어 가중치는 원격에 전달되지 않아 다른 클라이언트에서 회전 연출이 통째로 빠진다.
    /// </summary>
    [MenuItem("Tools/Animation/3. 플레이어 컨트롤러 구성")]
    public static void SetupPlayerController()
    {
        Log.D("[AnimationSetup] 플레이어 컨트롤러 구성 시작");

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(playerControllerPath);
        AnimatorStateMachine root = controller.layers[0].stateMachine;

        AnimatorState locomotion = FindState(root, "Locomotion");
        AnimationClip idle = FirstClipOf(locomotion.motion);

        EnsureFloatParameter(controller, paramMoveX);
        EnsureFloatParameter(controller, paramMoveY);
        EnsureFloatParameter(controller, paramTurnBlend);
        EnsureFloatParameter(controller, paramTurnMul);

        // 기존 블렌드 트리는 서브에셋으로 남으므로 교체 전에 지운다. 남기면 컨트롤러 파일에 고아가 쌓인다.
        DestroyBlendTrees(controller, locomotion.motion);

        BlendTree speedTree = NewTree(controller, "Locomotion", BlendTreeType.Simple1D, paramSpeed, paramSpeed);
        speedTree.useAutomaticThresholds = false;
        speedTree.AddChild(idle, 0f);
        speedTree.AddChild(BuildStrafeTree(controller, "Strafe Walk", "Strafe_Walk"), walkSpeed);
        speedTree.AddChild(BuildStrafeTree(controller, "Strafe Run", "Strafe_Run"), runSpeed);
        locomotion.motion = speedTree;

        // 제자리 회전. TurnBlend 는 정규화된 부호 있는 회전량이라 0 근처에서 자동으로 Idle 로 녹는다.
        AnimatorState turn = FindState(root, "Turn");
        if (turn == null)
        {
            turn = root.AddState("Turn", new Vector3(230f, 190f, 0f));
        }
        else
        {
            DestroyBlendTrees(controller, turn.motion);
            turn.transitions = Array.Empty<AnimatorStateTransition>();
        }

        BlendTree turnTree = NewTree(controller, "Turn", BlendTreeType.Simple1D, paramTurnBlend, paramTurnBlend);
        turnTree.useAutomaticThresholds = false;
        turnTree.AddChild(LoadClip("Turn_Left"), -1f);
        turnTree.AddChild(idle, 0f);
        turnTree.AddChild(LoadClip("Turn_Right"), 1f);
        turn.motion = turnTree;
        turn.speedParameterActive = true;
        turn.speedParameter = paramTurnMul;

        // |TurnBlend| 를 조건 하나로 쓸 수 없어 부호별로 두 갈래를 만든다.
        RemoveTransitionsTo(locomotion, turn);
        AddTurnEnter(locomotion, turn, AnimatorConditionMode.Greater, turnEnterBlend);
        AddTurnEnter(locomotion, turn, AnimatorConditionMode.Less, -turnEnterBlend);

        AnimatorStateTransition moved = turn.AddTransition(locomotion);
        ConfigureTransition(moved);
        moved.AddCondition(AnimatorConditionMode.Greater, turnEnterSpeed, paramSpeed);

        AnimatorStateTransition settled = turn.AddTransition(locomotion);
        ConfigureTransition(settled);
        settled.AddCondition(AnimatorConditionMode.Less, turnExitBlend, paramTurnBlend);
        settled.AddCondition(AnimatorConditionMode.Greater, -turnExitBlend, paramTurnBlend);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Log.D("[AnimationSetup] 플레이어 컨트롤러 구성 완료");
    }

    /// <summary>
    /// Mixamo 크라우치 클립 2종(Idle/Walking)을 공용 폴더로 추출한다.
    /// Rig 를 Humanoid 로 재설정한 뒤(Animation Type → Humanoid, Create Avatar From This Model)
    /// 그 서브에셋을 뽑는다 — ExtractClips() 와 완전히 같은 경로(FindClipInFbx)라 휴머노이드 근육 커브가 그대로 복제된다.
    /// loopBlend 를 강제로 켠다 — Mixamo 클립은 시작·끝 포즈가 정확히 안 맞아 껴 있으면 루프마다 포즈가 튄다.
    /// </summary>
    [MenuItem("Tools/Animation/5. 크라우치 클립 추출")]
    public static void ExtractCrouchClips()
    {
        Log.D("[AnimationSetup] 크라우치 클립 추출 시작");
        EnsureFolder(destFolder);

        int done = 0;
        foreach (ClipSource source in crouchClipSources)
        {
            AnimationClip src = FindClipInFbx(source.FbxPath, source.ClipName);
            if (src == null)
            {
                Debug.LogError($"[AnimationSetup] 크라우치 클립을 찾지 못함: {source.FbxPath} :: {source.ClipName}");
                continue;
            }

            string destPath = $"{destFolder}/{source.DestName}.anim";
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.loopBlend = true;
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);

            if (existing != null)
            {
                EditorUtility.CopySerialized(src, existing);
                existing.name = source.DestName;
                AnimationUtility.SetAnimationClipSettings(existing, settings);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AnimationClip copy = UnityEngine.Object.Instantiate(src);
                copy.name = source.DestName;
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                AssetDatabase.CreateAsset(copy, destPath);
            }

            done++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log.D($"[AnimationSetup] 크라우치 클립 추출 완료: {done}/{crouchClipSources.Length}");
    }

    /// <summary>
    /// 플레이어 컨트롤러에 크라우치 전용 로코모션 상태를 붙인다.
    /// IsCrouching 을 기존 Speed 트리에 얹지 않고 별도 상태로 완전히 분리한 이유: 웅크림 속도(1.5)가
    /// 서 있는 Walk 블렌드 지점과 겹쳐서, 분리하지 않으면 웅크린 채로 서 있는 Walk 클립이 재생된다.
    /// </summary>
    [MenuItem("Tools/Animation/6. 플레이어 크라우치 구성")]
    public static void SetupPlayerCrouchController()
    {
        Log.D("[AnimationSetup] 플레이어 크라우치 구성 시작");

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(playerControllerPath);
        AnimatorStateMachine root = controller.layers[0].stateMachine;

        AnimatorState locomotion = FindState(root, "Locomotion");

        EnsureBoolParameter(controller, paramIsCrouching);

        AnimatorState crouch = FindState(root, "CrouchLocomotion");
        if (crouch == null)
        {
            crouch = root.AddState("CrouchLocomotion", new Vector3(230f, 320f, 0f));
        }
        else
        {
            DestroyBlendTrees(controller, crouch.motion);
            crouch.transitions = Array.Empty<AnimatorStateTransition>();
        }
        RemoveTransitionsTo(locomotion, crouch);

        BlendTree crouchTree = NewTree(controller, "CrouchLocomotion", BlendTreeType.Simple1D, paramSpeed, paramSpeed);
        crouchTree.useAutomaticThresholds = false;
        crouchTree.AddChild(LoadClip("Crouch_Idle"), 0f);
        crouchTree.AddChild(LoadClip("Crouch_Walk"), crouchWalkSpeed);
        crouch.motion = crouchTree;

        AnimatorStateTransition enterCrouch = locomotion.AddTransition(crouch);
        ConfigureTransition(enterCrouch);
        enterCrouch.AddCondition(AnimatorConditionMode.If, 0f, paramIsCrouching);

        AnimatorStateTransition exitCrouch = crouch.AddTransition(locomotion);
        ConfigureTransition(exitCrouch);
        exitCrouch.AddCondition(AnimatorConditionMode.IfNot, 0f, paramIsCrouching);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Log.D("[AnimationSetup] 플레이어 크라우치 구성 완료");
    }

    /// <summary>
    /// 좀비 걷기 클립([RM] 변형)을 공용 폴더로 추출한다.
    /// ExtractClips() 와 완전히 같은 경로(FindClipInFbx) — 좀비 FBX 도 Humanoid 아바타라 그대로 복제된다.
    /// </summary>
    [MenuItem("Tools/Animation/7. 위장 클립 추출")]
    public static void ExtractDisguiseClips()
    {
        Log.D("[AnimationSetup] 위장 클립 추출 시작");
        EnsureFolder(destFolder);

        int done = 0;
        foreach (ClipSource source in disguiseClipSources)
        {
            AnimationClip src = FindClipInFbx(source.FbxPath, source.ClipName);
            if (src == null)
            {
                Debug.LogError($"[AnimationSetup] 위장 클립을 찾지 못함: {source.FbxPath} :: {source.ClipName}");
                continue;
            }

            string destPath = $"{destFolder}/{source.DestName}.anim";
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.loopBlend = true; // 시작·끝 포즈 불일치로 인한 루프 팝 방지(크라우치 클립과 같은 이유)
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);

            if (existing != null)
            {
                EditorUtility.CopySerialized(src, existing);
                existing.name = source.DestName;
                AnimationUtility.SetAnimationClipSettings(existing, settings);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AnimationClip copy = UnityEngine.Object.Instantiate(src);
                copy.name = source.DestName;
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                AssetDatabase.CreateAsset(copy, destPath);
            }

            done++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log.D($"[AnimationSetup] 위장 클립 추출 완료: {done}/{disguiseClipSources.Length}");
    }

    /// <summary>
    /// 플레이어 컨트롤러에 위장 전용 상태를 붙인다. 위장 중엔 시선 방향 고정속도 자동전진뿐이라
    /// (조작상호작용UI.md 2-2, disguise.csv move_speed 1.2) 블렌드 트리 없이 단일 루프 클립으로 충분하다.
    /// </summary>
    [MenuItem("Tools/Animation/8. 플레이어 위장 구성")]
    public static void SetupPlayerDisguiseController()
    {
        Log.D("[AnimationSetup] 플레이어 위장 구성 시작");

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(playerControllerPath);
        AnimatorStateMachine root = controller.layers[0].stateMachine;

        AnimatorState locomotion = FindState(root, "Locomotion");

        EnsureBoolParameter(controller, paramIsDisguised);

        AnimatorState disguise = FindState(root, "Disguise");
        if (disguise == null)
        {
            disguise = root.AddState("Disguise", new Vector3(230f, 450f, 0f));
        }
        else
        {
            DestroyBlendTrees(controller, disguise.motion);
            disguise.transitions = Array.Empty<AnimatorStateTransition>();
        }
        RemoveTransitionsTo(locomotion, disguise);

        disguise.motion = LoadClip("Disguise_Walk");

        AnimatorStateTransition enterDisguise = locomotion.AddTransition(disguise);
        ConfigureTransition(enterDisguise);
        enterDisguise.AddCondition(AnimatorConditionMode.If, 0f, paramIsDisguised);

        AnimatorStateTransition exitDisguise = disguise.AddTransition(locomotion);
        ConfigureTransition(exitDisguise);
        exitDisguise.AddCondition(AnimatorConditionMode.IfNot, 0f, paramIsDisguised);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Log.D("[AnimationSetup] 플레이어 위장 구성 완료");
    }

    /// <summary>좀비 컨트롤러에 다리 전용 Turn 레이어를 붙인다. 가중치는 ZombieAnimator 가 회전량으로 직접 준다.</summary>
    [MenuItem("Tools/Animation/4. 좀비 Turn 레이어 구성")]
    public static void SetupZombieTurnLayer()
    {
        Log.D("[AnimationSetup] 좀비 Turn 레이어 구성 시작");

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(zombieControllerPath);
        AnimationClip idle = FirstClipOf(FindState(controller.layers[0].stateMachine, "Locomotion").motion);

        EnsureFloatParameter(controller, paramTurnBlend);
        EnsureFloatParameter(controller, paramTurnMul);

        int index = IndexOfLayer(controller, zombieTurnLayerName);
        if (index >= 0)
        {
            // 레이어를 지우면 소유하던 스테이트머신 서브에셋도 같이 정리해야 고아가 남지 않는다.
            AnimatorStateMachine old = controller.layers[index].stateMachine;
            DestroyBlendTrees(controller, FindState(old, "Turn")?.motion);
            controller.RemoveLayer(index);
            if (old != null) UnityEngine.Object.DestroyImmediate(old, true);
        }

        controller.AddLayer(zombieTurnLayerName);
        AnimatorControllerLayer[] layers = controller.layers;
        AnimatorControllerLayer layer = layers[layers.Length - 1];
        layer.avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(legsMaskPath);
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 0f; // 스크립트가 매 프레임 덮는다. 초기값 0 이라야 스폰 직후 다리가 튀지 않는다
        layers[layers.Length - 1] = layer;
        controller.layers = layers;

        AnimatorState turn = layer.stateMachine.AddState("Turn", new Vector3(260f, 60f, 0f));
        BlendTree tree = NewTree(controller, "Turn", BlendTreeType.Simple1D, paramTurnBlend, paramTurnBlend);
        tree.useAutomaticThresholds = false;
        tree.AddChild(LoadClip("Turn_Left"), -1f);
        tree.AddChild(idle, 0f); // 부호가 바뀌는 순간의 팝을 막는 중립 포즈. 이 지점의 레이어 가중치는 0 에 가깝다
        tree.AddChild(LoadClip("Turn_Right"), 1f);
        turn.motion = tree;
        turn.speedParameterActive = true;
        turn.speedParameter = paramTurnMul;
        layer.stateMachine.defaultState = turn;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Log.D($"[AnimationSetup] 좀비 Turn 레이어 구성 완료 (index {controller.layers.Length - 1})");
    }

    /// <summary>
    /// 8방향 2D Freeform Cartesian 트리를 만든다. Invector 스트레이프 셋에 좌측 클립이 없어
    /// 우측 클립을 Mirror 로 뒤집고, 좌우 발이 뒤바뀌는 만큼 CycleOffset 0.5 로 보폭 위상을 되맞춘다.
    /// </summary>
    /// <param name="controller">트리를 서브에셋으로 담을 컨트롤러.</param>
    /// <param name="treeName">트리 이름.</param>
    /// <param name="prefix">클립 이름 접두사(Strafe_Walk / Strafe_Run).</param>
    /// <returns>구성된 블렌드 트리.</returns>
    private static BlendTree BuildStrafeTree(AnimatorController controller, string treeName, string prefix)
    {
        BlendTree tree = NewTree(controller, treeName, BlendTreeType.FreeformCartesian2D, paramMoveX, paramMoveY);

        tree.AddChild(LoadClip(prefix + "Forward"), new Vector2(0f, 1f));
        tree.AddChild(LoadClip(prefix + "Back"), new Vector2(0f, -1f));
        tree.AddChild(LoadClip(prefix + "ForwardRight"), new Vector2(1f, 1f));
        tree.AddChild(LoadClip(prefix + "BackRight"), new Vector2(1f, -1f));
        tree.AddChild(LoadClip(prefix + "Right"), new Vector2(1f, 0f));
        tree.AddChild(LoadClip(prefix + "ForwardRight"), new Vector2(-1f, 1f));
        tree.AddChild(LoadClip(prefix + "BackRight"), new Vector2(-1f, -1f));
        tree.AddChild(LoadClip(prefix + "Right"), new Vector2(-1f, 0f));

        ChildMotion[] children = tree.children;
        for (int i = 5; i < children.Length; i++)
        {
            children[i].mirror = true;
            children[i].cycleOffset = 0.5f;
        }
        tree.children = children;

        return tree;
    }

    /// <summary>컨트롤러 서브에셋으로 빈 블렌드 트리를 만든다.</summary>
    /// <param name="controller">소유 컨트롤러.</param>
    /// <param name="name">트리 이름.</param>
    /// <param name="type">블렌드 방식.</param>
    /// <param name="parameterX">X 축 파라미터.</param>
    /// <param name="parameterY">Y 축 파라미터.</param>
    /// <returns>생성된 블렌드 트리.</returns>
    private static BlendTree NewTree(AnimatorController controller, string name, BlendTreeType type, string parameterX, string parameterY)
    {
        BlendTree tree = new BlendTree
        {
            name = name,
            hideFlags = HideFlags.HideInHierarchy,
            blendType = type,
            blendParameter = parameterX,
            blendParameterY = parameterY,
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    /// <summary>모션 트리를 재귀적으로 파괴한다. 클립은 외부 에셋이라 건드리지 않는다.</summary>
    /// <param name="controller">소유 컨트롤러.</param>
    /// <param name="motion">파괴할 루트 모션.</param>
    private static void DestroyBlendTrees(AnimatorController controller, Motion motion)
    {
        if (motion is not BlendTree tree) return;

        foreach (ChildMotion child in tree.children) DestroyBlendTrees(controller, child.motion);
        UnityEngine.Object.DestroyImmediate(tree, true);
    }

    /// <summary>모션에서 첫 애니메이션 클립을 찾는다. 블렌드 트리면 재귀로 내려간다.</summary>
    /// <param name="motion">탐색 대상 모션.</param>
    /// <returns>찾은 클립. 없으면 null.</returns>
    private static AnimationClip FirstClipOf(Motion motion)
    {
        if (motion is AnimationClip clip) return clip;
        if (motion is not BlendTree tree) return null;

        foreach (ChildMotion child in tree.children)
        {
            AnimationClip found = FirstClipOf(child.motion);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>추출 폴더의 클립을 이름으로 읽는다.</summary>
    /// <param name="name">확장자를 뺀 클립 파일 이름.</param>
    /// <returns>읽은 클립.</returns>
    private static AnimationClip LoadClip(string name)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{destFolder}/{name}.anim");
        if (clip == null) Debug.LogError($"[AnimationSetup] 추출된 클립 없음: {name}. 먼저 1번 메뉴를 실행할 것");
        return clip;
    }

    /// <summary>FBX 안의 서브에셋 클립을 이름으로 찾는다.</summary>
    /// <param name="fbxPath">FBX 에셋 경로.</param>
    /// <param name="clipName">서브에셋 클립 이름.</param>
    /// <returns>찾은 클립. 없으면 null.</returns>
    private static AnimationClip FindClipInFbx(string fbxPath, string clipName)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
        {
            if (asset is AnimationClip clip && clip.name == clipName) return clip;
        }
        return null;
    }

    /// <summary>상태 머신에서 이름으로 상태를 찾는다.</summary>
    /// <param name="machine">탐색할 상태 머신.</param>
    /// <param name="name">상태 이름.</param>
    /// <returns>찾은 상태. 없으면 null.</returns>
    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == name) return child.state;
        }
        return null;
    }

    /// <summary>이름으로 레이어 인덱스를 찾는다.</summary>
    /// <param name="controller">대상 컨트롤러.</param>
    /// <param name="name">레이어 이름.</param>
    /// <returns>레이어 인덱스. 없으면 -1.</returns>
    private static int IndexOfLayer(AnimatorController controller, string name)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name == name) return i;
        }
        return -1;
    }

    /// <summary>같은 이름의 float 파라미터가 없으면 추가한다.</summary>
    /// <param name="controller">대상 컨트롤러.</param>
    /// <param name="name">파라미터 이름.</param>
    private static void EnsureFloatParameter(AnimatorController controller, string name)
    {
        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.name == name) return;
        }
        controller.AddParameter(name, AnimatorControllerParameterType.Float);
    }

    /// <summary>같은 이름의 bool 파라미터가 없으면 추가한다.</summary>
    /// <param name="controller">대상 컨트롤러.</param>
    /// <param name="name">파라미터 이름.</param>
    private static void EnsureBoolParameter(AnimatorController controller, string name)
    {
        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.name == name) return;
        }
        controller.AddParameter(name, AnimatorControllerParameterType.Bool);
    }

    /// <summary>특정 상태로 향하는 전이를 모두 제거한다. 재실행 시 전이가 중복 쌓이는 것을 막는다.</summary>
    /// <param name="from">출발 상태.</param>
    /// <param name="to">도착 상태.</param>
    private static void RemoveTransitionsTo(AnimatorState from, AnimatorState to)
    {
        List<AnimatorStateTransition> kept = new List<AnimatorStateTransition>();
        foreach (AnimatorStateTransition transition in from.transitions)
        {
            if (transition.destinationState == to) continue;
            kept.Add(transition);
        }
        from.transitions = kept.ToArray();
    }

    /// <summary>정지 상태에서 회전량이 문턱을 넘을 때 Turn 으로 들어가는 전이를 만든다.</summary>
    /// <param name="from">Locomotion 상태.</param>
    /// <param name="to">Turn 상태.</param>
    /// <param name="mode">TurnBlend 비교 방식(부호별로 다르다).</param>
    /// <param name="threshold">TurnBlend 문턱값.</param>
    private static void AddTurnEnter(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        ConfigureTransition(transition);
        transition.AddCondition(AnimatorConditionMode.Less, turnEnterSpeed, paramSpeed);
        transition.AddCondition(mode, threshold, paramTurnBlend);
    }

    /// <summary>전이 공통 설정. 제자리 회전은 즉시 반응해야 하므로 종료 시간을 쓰지 않는다.</summary>
    /// <param name="transition">설정할 전이.</param>
    private static void ConfigureTransition(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.15f;
        transition.exitTime = 0f;
        transition.canTransitionToSelf = false;
    }

    /// <summary>중간 경로를 포함해 폴더를 만든다.</summary>
    /// <param name="path">Assets 로 시작하는 폴더 경로.</param>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
