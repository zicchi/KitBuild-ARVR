# ConceptMapAR

Aplikasi AR (Unity 6 + AR Foundation) untuk menyusun peta konsep di ruang 3D.
Node **Konsep** (kuning) dan **Relasi** (abu-abu) melayang di depan kamera dan
bisa disambungkan menjadi proposisi `Konsep --[relasi]--> Konsep`.

## Fitur

- Muat peta konsep dari file JSON (file picker Android), clipboard (JSON/URL), atau API
- Sambungkan node: drag tombol → / ← ke node tujuan, atau tap tombol lalu tap node
- Hapus semua koneksi sebuah relasi (tombol X)
- Pindahkan node dengan drag, pinch untuk mengubah ukuran semua node
- Undo / Redo
- Rapikan node ke grid, pusatkan peta ke depan kamera
- Indikator tepi layar untuk node yang berada di luar pandangan
- Simpan hasil ke `Download/ConceptMapAR/` (Android) atau `persistentDataPath`

## Kebutuhan

- Unity **6000.4.6f1**
- Android (ARCore, min API 29) atau iOS (ARKit)

## Menjalankan

1. Buka folder project lewat Unity Hub.
2. Buka scene `Assets/Scenes/Main.unity`.
3. Pilih platform Android/iOS di **File → Build Profiles**, lalu build.

Build APK dari command line:

```bash
Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod BuildAndroid.Build
```

Hasilnya ada di `Builds/ConceptMapAR.apk`.

## Sumber data

`ApiLoader` (di scene) menentukan peta awal:

| Field          | Keterangan                                                        |
|----------------|-------------------------------------------------------------------|
| `useSampleMap` | `true` → pakai `Assets/StreamingAssets/map.json`                  |
| `apiBaseUrl`   | Base URL API, dipanggil sebagai `GET {apiBaseUrl}/maps/{mapId}`   |
| `mapId`        | ID peta konsep                                                    |

Bila API gagal, aplikasi otomatis memakai `map.json`.

## Format JSON

```json
{
  "canvas": {
    "concepts":    [{ "cid": "c1", "label": "Gempa Bumi", "x": 280, "y": 130, "data": "{...}" }],
    "links":       [{ "lid": "l1", "label": "menyebabkan", "x": 460, "y": 160, "source_cid": "c1", "data": "{...}" }],
    "linktargets": [{ "lid": "l1", "target_cid": "c2", "target_data": "{...}" }],
    "concepts_ext": [], "links_ext": [], "linktargets_ext": []
  },
  "map": { "cmid": "...", "direction": "multi" }
}
```

Koordinat `x`/`y` adalah koordinat kanvas 2D; saat dimuat, peta diskalakan
otomatis agar pas di depan kamera.

## Struktur script

| File                         | Peran                                                    |
|------------------------------|----------------------------------------------------------|
| `ConceptMapManager.cs`       | Controller utama: load/save, spawn node, koneksi, undo   |
| `MapNode.cs`                 | Base node: billboard, auto-size, gesture, tombol koneksi |
| `ConceptNode.cs` / `LinkNode.cs` | Node konsep / relasi                                 |
| `ConnectionLine.cs`          | Garis koneksi (titik untuk source, panah untuk target)   |
| `AppToolbar.cs`              | Toolbar UI, panel muat, overlay JSON, toast              |
| `ToolbarIcons.cs`            | Ikon toolbar prosedural                                  |
| `OffscreenIndicator.cs`      | Indikator node di luar layar                             |
| `ApiLoader.cs`               | Memuat peta awal dari API / `map.json`                   |
| `ConceptMapData.cs`          | Model data JSON                                          |
| `CameraPermissionHandler.cs` | Meminta izin kamera sebelum ARSession aktif              |
