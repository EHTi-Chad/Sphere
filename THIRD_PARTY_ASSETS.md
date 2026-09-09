# Third-party assets not tracked in git

These are excluded from version control (large, redistributable, not original project work).
Re-import them from the Asset Store into the matching `Assets/` folder if you need them.

- **Assets/Proxy Games/Stylized Nature Kit Lite/** — [Stylized Nature Kit Lite](https://assetstore.unity.com/packages/3d/environments/stylized-nature-kit-lite-176906) (Proxy Games)
- **Assets/Rocks and Boulders 2/** — [Rock and Boulders 2](https://assetstore.unity.com/packages/3d/props/exterior/rock-and-boulders-2-6947) (Manufactura K4)
- **Assets/TerrainSampleAssets/** — [Terrain Sample Asset Pack](https://assetstore.unity.com/packages/3d/environments/landscapes/terrain-sample-asset-pack-145808) (Unity Technologies)

## Also excluded (not asset packs, but too large for git)

- **Models/** — local LLM weight files (`.gguf`). Download whichever model you want to use and
  drop it in this folder; `LlamaCppBackend` picks up anything placed here.
- **Assets/StreamingAssets/LlamaLib-v2.0.5/** — native llama.cpp binaries for every platform.
  Reinstalled automatically by the LLMUnity package's own setup step.
