# BREAK TACTICS BossRush
**Phase 3-B｜追加コード解説**

## 1　追加・変更コード一覧

| ファイル | 追加したメソッド・フィールド等 | 役割 |
| --- | --- | --- |
| BossRushRunState.cs | `SkillsPerGroup`、`TotalSkills`、`MaxClassChanges`、`CurrentPlayer`、`EquippedSkills`、`ClassChangesUsed`、`RemainingClassChanges`、`IsInBattle` | 4枠と変更上限を定義し、確定済みの状態を保持する。 |
| BossRushRunState.cs | `CanChooseClass`、`TryBeginBattle`、`FinishBattle`、`IsRegistered` | 職業変更と4枠を検証して確定し、勝敗後に戦闘中の状態を解除する。 |
| BossRushPreparationUI.cs | `RoleCard`、`SkillCard`、`controller`、`roleCards`、`classCards`、`commonCards`、`selectedSlots`、`selectedRole`、`remainingChanges`、`message`、`confirm`、`selectedColor`、`cardColor` | Inspectorでボタン・画像・TMP表示と配色を接続する。 |
| BossRushPreparationUI.cs | `classSelection`、`commonSelection`、`draftPlayer`、`Awake`、`Show`、`Hide`、`BindSkillButtons` | 仮選択を保持し、ボタンと処理を接続して画面を開閉する。 |
| BossRushPreparationUI.cs | `ChooseRole`、`ToggleSkill`、`Confirm`、`Refresh`、`RefreshSkillCards`、`SetSelected` | 職業と技能を選び、4枠・選択色・エラーを表示し、確定を依頼する。 |
| BossRushBattleController.cs | `ClassOption`（`player`、`skills`、`Player`、`Skills`）、`preparationUI`、`classOptions`、`commonSkills`、`run`、`pendingEnemy` | 職業データ・候補を設定し、プレイ状態と開始予定の敵を管理する。 |
| BossRushBattleController.cs | `CurrentPlayer`、`EquippedSkills`、`ClassChangesUsed`、`RemainingClassChanges`、`IsInBattle`、`ClassOptions`、`CommonSkills` | 状態と候補をUIや呼び出し元へ公開する。 |
| BossRushBattleController.cs | `Start`（変更）、`OpenPreparation`、`CanChooseClass`、`GetClassCandidates`、`TryConfirmAndStart`、`OnBattleFinished`（処理追加） | 起動時に準備を開き、確定と1戦の開始をつなぐ。勝敗後に編成を変更できる状態へ戻す。 |
| PlayerManager.cs | `currentSkills`（型・初期値）、`Setup`（引数・アクセス範囲・代入）、`using System` / `using System.Linq` | `PlayerData.startSkills` からの補充をやめ、確定済み4スキルを既存の装備判定とUIへ渡す。 |
| SkillUseRules.cs | `IsAllowedForRole`（変更）、`IsClassSkill`、`IsCommonSkill` | 職業固有と共通のID判定を分け、編成検証と戦闘の職業判定で共有する。 |

比較元は実装直前の `Logs/Phase3-B-20261112-210004/Before`。既存3ファイルは `// 追加` と実装前後の差分を照合した。以下の括弧内の行番号は現在のソースを示す。

<!-- pagebreak -->
## 2　BossRushRunState.cs
**確定済みの編成と戦闘状態**

### 定数・プロパティ（6–14行）
`SkillsPerGroup` は各枠2つ、`TotalSkills` は合計4つ、`MaxClassChanges` は1回。職業・編成・使用回数・戦闘中フラグは `private set` 付きのプロパティとして保持する。読み取りは外からでき、代入はクラス内に限られる。`RemainingClassChanges` は上限から使用回数を引いた値を返す。


**BossRushRunState.cs / CanChooseClass（16–20行、抜粋）**

```csharp
    public bool CanChooseClass(PlayerData player)
    {
        return !IsInBattle && player != null &&
            (CurrentPlayer == null || CurrentPlayer.role == player.role || RemainingClassChanges > 0);
    }
```

`&&` は両辺、`||` はいずれかの成立を求める。戦闘外で職業データが存在し、「初回」「同じ職業」「変更枠が残っている」のどれかを満たすと `true`。たとえば戦士から魔法使いへ変更済みなら、魔法使いでの再編成は可、バーサーカーへの変更は不可。

### TryBeginBattle（22–89行）
戦闘中、職業未選択、職業変更不可、リストの欠落と枠数違いの順に早期終了する。続いて `SkillUseRules` で枠と職業を照合し、`IsRegistered` で登録済みの参照かを確かめる。たとえば固有枠に自由スキルを入れると、候補に混ざっていても拒否される。`false` と `out string error` で失敗と理由を呼び出し元へ返す。


**BossRushRunState.cs / TryBeginBattle（69–88行、抜粋）**

```csharp
            if (!skillIds.Add(classSkill.id) || !skillIds.Add(commonSkill.id))
            {
                error = "同じスキルを複数の枠に編成することはできません。";
                return false;
            }

            equipped[i] = classSkill;
            equipped[i + SkillsPerGroup] = commonSkill;
        }

        if (CurrentPlayer != null && CurrentPlayer.role != player.role)
        {
            ClassChangesUsed++;
        }

        CurrentPlayer = player;
        EquippedSkills = Array.AsReadOnly(equipped);
        IsInBattle = true;
        error = string.Empty;
        return true;
```

`HashSet<SkillId>` の `Add` は同じIDを再登録すると `false` を返す。4つを固有→自由の順に配列へ収め、全検証後に異職への変更回数を加算する。初回や同職での確定は加算されない。`Array.AsReadOnly` で配列を包むため、UIの仮選択リストを後から編集しても確定済みの並びに影響しない。ここで立つ戦闘中フラグをコントローラーも参照する。

### FinishBattle・IsRegistered（91–107行）
`FinishBattle` は勝敗後に `IsInBattle` を下ろす。確定した職業・装備・残り回数は次の準備で使える。`IsRegistered` は `foreach` で候補を巡回し、同じ `SkillData` 参照が見つかると `true` を返す。IDの分類と候補の参照一致は両方必要になる。

<!-- pagebreak -->
## 3　BossRushPreparationUI.cs
**画面上の選択を編成確定に届ける**

### カード・フィールド・Awake（9–54行）
`RoleCard` は職業ボタン、画像、名前、能力表示を束ね、`SkillCard` は技能ボタン、名前、説明、コスト表示を束ねる。`[Serializable]` と `[SerializeField]` でカードやUIの参照をInspectorに保存できる。`draftPlayer` と2本の `List<SkillData>` に仮選択を置く。可変長リストなので選ぶたびに追加・削除できる。


**BossRushPreparationUI.cs / Awake（45–49行、抜粋）**

```csharp
        for (int i = 0; i < roleCards.Length; i++)
        {
            int index = i;
            roleCards[i].button.onClick.AddListener(() => ChooseRole(index));
        }
```

`AddListener` はクリック時の処理を登録する。`() => ChooseRole(index)` は引数なしのラムダ式で、各周回の `index` を保持する。後でボタンを押すと、対応する職業の番号が渡る。

### Show → Hide → BindSkillButtons → ChooseRole（56–110行）
`Show` はコントローラーから確定済みの職業とスキルを取得し、共通IDかどうかで2本のリストへ戻して画面を表示する。`Hide` は画面を非表示にする。`BindSkillButtons` はカードの番号と固有枠かどうかをクリック処理に渡す。`ChooseRole` は選択可否をコントローラーに問い合わせ、別の職業を仮選択したときに固有枠だけを空にする。自由枠は引き継ぐ。


**BossRushPreparationUI.cs / ToggleSkill（124–133行、抜粋）**

```csharp
        if (!selection.Remove(skill))
        {
            if (selection.Count >= BossRushRunState.SkillsPerGroup)
            {
                message.text = "この枠は2つまでです。選択済みのスキルを押すと外せます。";
                return;
            }

            selection.Add(skill);
        }
```

`ToggleSkill` は職業の選択を先に要求する。`条件 ? A : B` は固有・自由のどちらの候補とリストを使うか選ぶ。`Remove` が成功すれば解除し、未選択なら空きがあるときに追加する。3つ目を押すとメッセージを出して選択を保つ。


**BossRushPreparationUI.cs / Confirm（139–145行、抜粋）**

```csharp
    private void Confirm()
    {
        if (!controller.TryConfirmAndStart(draftPlayer, classSelection, commonSelection, out string error))
        {
            message.text = error;
        }
    }
```

`Confirm` は仮選択をコントローラーへ渡す。`out` で受け取ったエラーをTMPの `message.text` に表示する。

### Refresh → RefreshSkillCards → SetSelected（147–209行）
`Refresh` は仮選択中の職業・残り回数・選択可能な職業を表示し、前半2枠に固有、後半2枠に自由、空欄に「未選択」を入れる。`RefreshSkillCards` は候補の名前・説明・MP/CDを表示し、候補のないカードを隠す。`SetSelected` はボタンの色を選択状態に合わせる。

<!-- pagebreak -->
## 4　BossRushBattleController.cs
**準備画面と1戦をつなぐ**

### ClassOption・設定と状態（7–37行）
`ClassOption` は1職分の `PlayerData` と固有候補。`classOptions` と `commonSkills` はInspectorの登録先、`run` は確定済みの状態、`pendingEnemy` は開始予定の敵を表す。`=>` で書いた取得専用プロパティで、UIへ候補・装備・変更回数などを渡す。

### Start → OpenPreparation → 候補参照（55–98行）
`Start` は `OpenPreparation(enemyData)` を呼ぶ。`OpenPreparation` は戦闘中や参照の不足で `false` を返し、開けるときは敵データを一時保存して `Show` を呼ぶ。`CanChooseClass` は `run` に判定を委ね、`GetClassCandidates` は同じ `PlayerData` が登録された固有候補を返す。対応する登録がなければ空の配列を返す。


**BossRushBattleController.cs / TryConfirmAndStart（128–134行、抜粋）**

```csharp
        EnemyData encounter = pendingEnemy;
        pendingEnemy = null;
        preparationUI.Hide();
        battleManager.player.Setup(run.CurrentPlayer, run.EquippedSkills);
        EnemyManager enemy = Instantiate(enemyPrefab);
        enemy.Setup(encounter);
        battleManager.Setup(enemy);
```

`TryConfirmAndStart` は戦闘中・準備未開始を拒否する。`TryGetComponent` は敵Prefabのコンポーネントの有無を `bool` で、取得結果を `out` で返す。編成が有効なら準備画面を閉じ、プレイヤーを初期化する。敵の生成・初期化から `BattleManager.Setup` へ進む既存処理は、確定後に移った。


**BossRushBattleController.cs / OnBattleFinished（141–145行、抜粋）**

```csharp
        // 追加
        run.FinishBattle();

        SoundManager.instance.StopBGM();
        Debug.Log($"ボスラッシュの戦闘結果：{(result == BattleResult.Victory ? "勝利" : "敗北")}", this);
```

`run.FinishBattle()` が今回の追加。実装前から `OnEnable` と `OnDisable` が `+=` / `-=` で勝敗イベントを購読・解除し、結果受信時にBGMを止めてログを出していた。`event Action<BattleResult>` は結果1個を引数に取る通知口で、`BattleManager` が発行し、このコントローラーが受け取る。

## 5　PlayerManager.cs
### currentSkills → Setup（22、31–67行）
`currentSkills` は空の `IReadOnlyList<SkillData>` で始まる。既存の `Setup` は `internal`（同じアセンブリ内から呼べる）になり、職業データに加え、検証済み装備を受け取る。


**PlayerManager.cs / Setup（63–66行、抜粋）**

```csharp
        // 追加
        currentSkills = equippedSkills;

        UpdateSkillPanel();
```

既存の初期化で職業・画像・能力値・HP/MP・毒・CD・戦闘状態をセットまたはリセットし、その後に装備を代入する。実装前は `data.startSkills` をコピーしていた。未使用だった `MaxSkillCount` も削除された。既存の `UpdateSkillPanel` と `SkillPanelPager` は `Skills` を表示し、`HasSkill` はその装備で所持を判定する。

<!-- pagebreak -->
## 6　SkillUseRules.cs
**枠の分類を編成と戦闘で共有する**

### IsAllowedForRole → IsClassSkill（11–35行）
既存の `IsAllowedForRole` は、`SkillId.None` を許容する従来の分岐を残し、固有・共通の判定を新しいメソッドに委ねる。`IsClassSkill` は `null` を弾き、`switch (role)` で3職それぞれのID範囲を判定する。たとえば `Warrior_BreakSlash` は戦士では `true`、魔法使いでは `false`。


**SkillUseRules.cs / IsClassSkill（24–29行、抜粋）**

```csharp
        switch (role)
        {
            case PlayerRole.Warrior:
                return skill.id >= SkillId.Warrior_BreakSlash && skill.id <= SkillId.Warrior_TacticalSlash;
            case PlayerRole.Mage:
                return skill.id >= SkillId.Mage_FlamePiercer && skill.id <= SkillId.Mage_ArcaneBurst;
```


**SkillUseRules.cs / IsCommonSkill（37–41行、抜粋）**

```csharp
    // 追加
    public static bool IsCommonSkill(SkillData skill)
    {
        return skill != null && skill.id >= SkillId.Common_Guard && skill.id <= SkillId.Common_PartProbe;
    }
```

`static` メソッドはインスタンスを作らず `SkillUseRules.IsCommonSkill(skill)` のように呼べる。先に `null` を調べ、参照がなければ `&&` の右側を評価しない。`IsCommonSkill` はIDの分類を担当し、自由枠への登録は `BossRushRunState` が別途確認する。戦闘時の `CanUse`・`TryPay` は既存処理で、装備・職業・MP・CDなどを判定する。

## 7　全体の処理フロー

| 段階 | 担当クラスと処理 |
| --- | --- |
| ゲーム起動 | BossRushBattleController が準備画面を開く。 |
| ↓ 職業選択 | BossRushPreparationUI が職業を仮選択する。 |
| ↓ 固有2＋自由2を選択 | BossRushPreparationUI が候補と4枠を更新する。 |
| ↓ 編成を検証 | BossRushRunState が職業変更の可否を先に判定し、枠数・分類・登録・重複を確認する。 |
| ↓ 職業変更回数を確定 | BossRushRunState が異職への変更時だけ加算し、編成を保存する。 |
| ↓ 4スキルを反映 | PlayerManager が職業データで初期化し、確定した装備を反映する。 |
| ↓ 戦闘開始 | BossRushBattleController が敵を準備し、BattleManager が1戦を始める。 |
| ↓ 勝敗通知・状態解除 | BattleManager が勝敗を通知し、BossRushBattleController が BossRushRunState の戦闘中フラグを解除する。 |

Phase 3-Bでは、戦闘前に戦士・魔法使い・バーサーカーから職業を選び、固有2つと自由2つのスキルで1戦に挑めるようになった。仮選択は画面内で扱い、確定時に編成の適合と重複を検証する。戦闘UIと使用判定は確定した4枠を参照する。初回は職業変更枠を消費せず、戦闘間に別の職業へ確定できるのは1回。勝敗後は編成を保持したまま戦闘中の状態が解除され、次の準備画面を開く処理に接続できる。3連戦の自動進行はPhase 3-Cの範囲。
