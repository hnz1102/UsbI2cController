# 個々のバイトデータのWrite/Read設定機能

## 概要
各コマンドシーケンス内で、個々のバイトデータに対してWrite/Readを指定できる機能を追加しました。
デバイスアドレスの部分は常にWriteのみとなります。

## 主な変更点

### 1. モデルの拡張 (Models/I2CCommand.cs)

新しいクラスを追加：
- `ByteOperation` 枚挙型: Write/Readの動作を定義
- `ByteData` クラス: 個々のバイトのデータと動作を管理
- `I2COperation.ByteDataList`: 個々のバイトデータのリスト

```csharp
public enum ByteOperation
{
    Write,  // このバイトは書き込み
    Read    // このバイトは読み込み
}

public class ByteData
{
    public byte Value { get; set; }
    public ByteOperation Operation { get; set; }
    public byte? ReadResult { get; set; }
}
```

### 2. ViewModelの拡張 (ViewModels/CommandOperationViewModel.cs)

- `ByteDataViewModel`: 個々のバイトデータを管理するViewModel
- `CommandOperationViewModel.ByteDataList`: バイトデータのコレクション
- `UpdateLegacyData()`: 後方互換性のためWriteData/ReadLengthを更新

### 3. 新しい編集ダイアログ (Views/EditByteDataDialog)

個々のバイトデータを編集するためのダイアログを新規作成：

**主な機能：**
- デバイスアドレス入力 (Write固定)
- Write/Readバイトの追加
- 各バイトの操作タイプ選択 (ComboBox)
- Writeバイトの値入力
- バイトの削除
- 全クリア

**UI構成：**
```
┌─────────────────────────────────────┐
│ デバイスアドレス: [0x50] (Write固定)  │
├─────────────────────────────────────┤
│ [Writeバイト追加] [Readバイト追加]   │
├─────────────────────────────────────┤
│ 1 [Write▼] [0x1A      ] [削除]      │
│ 2 [Read ▼] [--------  ] [削除]      │
│ 3 [Write▼] [0x2B      ] [削除]      │
└─────────────────────────────────────┘
```

### 4. コマンド実行ロジックの更新 (Services/I2CCommandExecutor.cs)

`ExecuteByteDataSequence()` メソッドを追加：
- Writeバイトを収集して一括書き込み
- Readバイトの位置を記録
- 読み込み後、結果を対応するバイトに格納

### 5. コマンド追加/編集の更新 (ViewModels/MainViewModel.cs)

**AddWriteOperation/AddReadOperation:**
- 旧ダイアログの代わりに新しいEditByteDataDialogを使用
- デフォルトで1バイトのWrite/Read操作を追加

**EditOperation:**
- Write/ReadタイプはByteDataDialogで編集
- 既存データがない場合、旧形式から自動変換
- Delay/START/STOPは従来の簡易ダイアログで編集

### 6. データ保存/読み込み

**保存形式の拡張:**
```json
{
  "Operations": [
    {
      "Type": "Write",
      "DeviceAddress": "0x50",
      "ByteDataList": [
        {
          "Index": 1,
          "Operation": "Write",
          "ValueInput": "0x1A",
          "Value": 26
        },
        {
          "Index": 2,
          "Operation": "Read"
        }
      ]
    }
  ]
}
```

### 7. Converterの追加 (Converters/ValueConverters.cs)

- `EnumToIndexConverter`: Enum↔ComboBoxIndex変換
- `OperationToWriteEnabledConverter`: Read時に値入力を無効化

## 使用方法

### 新しいコマンドの追加

1. [Write追加]または[Read追加]ボタンをクリック
2. EditByteDataDialogが表示される
3. デバイスアドレスを入力（Write固定）
4. [Writeバイト追加]または[Readバイト追加]でバイトを追加
5. 各バイトの操作タイプ（Write/Read）を選択
6. Writeバイトの場合は値を入力
7. [OK]をクリック

### 既存コマンドの編集

1. コマンド行をダブルクリックまたは編集ボタンをクリック
2. Write/Read操作の場合、EditByteDataDialogが表示される
3. バイトの追加・削除・変更を行う
4. [OK]をクリック

## 後方互換性

- 旧形式（WriteData/ReadLength）のコマンドも引き続きサポート
- 旧形式のコマンドを編集時に新形式に自動変換
- 新形式のコマンドも旧形式のプロパティを保持（UpdateLegacyData）

## デバイスアドレスについて

デバイスアドレスは常にWriteのみです。これはI2Cプロトコルの仕様に基づいています：
- START条件後、最初のバイトは必ずデバイスアドレス+R/Wビット
- R/Wビットは通信方向を示す
- 実際のバイトデータのWrite/Readはその後に続く

## リソース文字列

日本語 (Strings.ja.xaml) と英語 (Strings.en.xaml) の両方に以下を追加：
- EditByteDataTitle: "バイトデータ編集"
- AddByte: "Writeバイト追加"
- AddReadByte: "Readバイト追加"
- ByteValueHint: "値 (例: 0x1A または 26)"
- 各種Tooltip

## テスト推奨項目

1. ✓ 新しいWrite操作の追加
2. ✓ 新しいRead操作の追加
3. ✓ 混合Write/Read操作の作成
4. ✓ バイトの追加・削除
5. ✓ Write/Readの切り替え
6. ✓ コマンドシーケンスの保存・読み込み
7. ✓ 旧形式データからの移行
8. ビルド成功確認 ✓
9. 実機でのコマンド実行（要FT232Hデバイス）
