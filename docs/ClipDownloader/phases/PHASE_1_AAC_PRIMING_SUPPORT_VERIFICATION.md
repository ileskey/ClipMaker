# Phase 1 후속 — AAC 시작 보정 지원·검증

2026-10-14. 시작 HEAD `367fd35`, branch `phase/1`, clean에서 사용자 “다음 작업 진행하자” 지시로 [이전 진단](PHASE_1_YOUTUBE_AAC_SKIP_DIAGNOSIS.md)의 실제 지원 보완을 수행했다. 일반 정책 재승인·새 패키지/도구 설치·전체 다운로드 fallback·Phase2/WPF 다운로드 UI/ClipEditor 변경은 없다. 원본 지원 코드 `2724256`, 출력 지원 코드 `7adbd74`다.

**신고된 YouTube wFOjWCLPDDM의137/140·25fps, IN0/OUT00:00:14:24 요청을 Fast와 Accurate 모두 실제 CLI에서 성공·최종 확정했다.** 새로운 지원은 명시적으로 검증한 AAC LC의 시작 edit/skip 대응이다. 임의 AAC/edit/padding 지원 또는 Phase1 전체 완료로 확대 해석하지 않는다.

## 실제 관찰·계약 보완

초기 AAC track은44100Hz, rate1 단일 content edit MediaTime1600이며 첫 packet skip1600/각reason0/padding0이다. native sample과 packet430개가 decoded frame429개로 변환된다. 첫1024 sample packet 전체와 다음 packet의576 sample이 소비되어 첫 유효 frame은PTS0/448 samples다.

추가64KiB prefix 대조에서 SIDX 음성 첫 범위가 `[0,7312/735)`임을 확인했다. 이는 보정 이후 유효 decoded native 범위와 정확히 같았다. coded packet의 음수 PTS를 사용자 원점으로 승격하지 않고, 실제 유효 native 범위를 source 원점/coverage 연결에 사용한다. NativeEnd는Unknown을 유지한다.

공유 native gate는 sample↔packet bytes/위치/PTS/DTS/duration/flags를 계속 검사한다. AAC의 첫 coded PTS/DTS0·단일 rate1 edit·정확한 leading skip metadata와1024 sample의 연속된 보정 prefix를 요구한다. 완전히 소모된 packet을 제외하고 부분 첫 frame의PTS/duration/nb_samples를 정확히 예측해 모든 decoded frame과 대조한다. `LeadingSkippedSamples`를 scoped native 관찰에 보존한다. 미일치/누락된 skip, 잘못된 frame·packet, 보정 중 gap·알 수 없는 side data를 성공시키지 않는다. 끝 discard padding, 비지원 edit/codec/clock과 모든 음성을 소비하는 입력은 지원으로 추정하지 않는다.

첫 지원 실험은 native gate를 통과한 뒤 provider payload bridge에서 실패했다. 보존한 참조를 로컬 관찰해 packed AAC ID3 timestamp914·302frames와 원본 세 번째 coded sample(PTS2048)의 payload 대응을 확인했다. 원본 movie PTS는2048−1600=448, `floor(448*90000/44100)=914`로 정확히 일치했다. 앞의 보정 packet을 임의로 검색·clamp하거나90kHz 양자화를 audio sample-grid 반올림으로 대체하지 않는다.

provider bridge는 native movie PTS가0 이상인 **최초의 whole packet**부터 시작하는 연속 payload와 기존 ID3 clock 양자화 관계를 요구한다. `FirstSourceSample=2`를 증거에 보존한다. provider가 첫 부분 frame을 포함하지 않았다는 이유로 원본의 유효448 samples를 zero로 바꾸지 않는다. 선택 source의 단일 edit/검증 skip, master group 선언, 원본/provider encoded content·video decoded picture 관계와 SIDX/native 범위 검사를 모두 유지했다.

Accurate 준비는 native 보정이 증명된 부분 첫 frame만 허용하고, 다른 부분 frame·증거 없는 trim은 거절한다. captured source-start prefix의 pre/post byte identity와 실제 전체 source-start decode를 유지한다. decoded sample 수는실제 frame의 sample count를 합산하며 PCM render는 기존 sample-grid/zero-gap/first-wins 계약을 따른다. lossless render와 lossy AAC 출력의 값 동일성을 구별한다.

Fast 최초 실행은 source·acquisition·copy를 통과했지만 `OutputPacketContradicted`에서 실패해 final을 만들지 않았다. 별도 진단 staging을 보존해 원본과 같은 output edit/skip1600, 첫 fully consumed packet의KD flag와 다음448-sample frame을 확인했다. 출력 gate의1024 skip 상한을 여러 packet에 걸친 정확한 보정 대응으로 확장했다. 유효 음성 packet의D flag는 계속 거절하며 header/side skip/decoded support/end 관계를 모두 확인한다. payload·timestamp·codec·picture/요청 coverage 검사를 생략하지 않는다.

## 실제 최종 결과

각 요청은 별도 고유 ignored 출력과 명시 mode/--yes로 실행했다. budget은기존32MiB/30초이며 요청 길이는374/25=14.96초다. 최초 Fast 실패·중간 provider 실패는 최종 성공으로 세지 않는다.

| 모드 | 실제 사용자 영상 범위 | 실제 사용자 음성 범위 | AAC skip/tail | final bytes/SHA256 |
|---|---|---|---|---|
| Fast | `[0,377/25)` | `[0,7856/525)` | 1600 / 0 | 730538 / `8c64e7b5932005cc5a6781a0df10b1360d768f00b914424696af9fe27c0166ba` |
| Accurate | `[0,374/25)` | `[0,374/25)` | 1024 / 744 | 1117080 / `03bf09349ab074db9616f07ac0a077ada4b90b58264c144f2c9d84b37ea8a768` |

Fast의 실제 표시 끝이 요청OUT과 다르다는 것을 기존 정책대로 표시했고, Accurate 영상·음성은 요청 범위와 일치했다. 새 자동mode/quality 변경은 없다. 양CLI는exit0으로 final을 확정하고 원본/PCM/staging의 일반 작업 폴더를 정리했다. 실제app-data root에서 이context의 알려진CLI job ID12개를 읽기 검사해 각 폴더0을 확인했다. source/PCM진단은 고유ignored폴더에 의도적으로 보존한 자료이며 CLI cleanup잔존과 혼동하지 않는다. 사용자out.mp4의 생성·덮어쓰기·삭제는 하지 않았다.

새 final2개와 기존6단계의 CHZZK/YouTube Fast/Accurate4개를 최신output gate로 재probe했고, 총6개native 검사·전체A/V decode 성공과hash 불변을 확인했다. 이를 기존사이트의 재다운로드 성공으로 세지 않는다.

## 독립 PCM oracle

보존한 원본에서 별도FFmpeg 호출로 float32 stereo를 decode해 요청의659736 samples=5277888 bytes를 비교했다. Fast final의 같은 범위 decoded PCM과 Accurate encoder 입력render PCM 모두 원본prefix와byte/hash가 일치했다.

`B57B7D5F5A79DB9B48B91CAE544C3717410CE349EA4F0D114440B2180E4D9810`

Accurate source-start decoded PCM 전체도 독립decode와 일치했으며 render의zero 삽입/overlap 제거는0/0이었다. 이 일치는 이번captured source/요청 범위에 제한한다. Accurate final AAC는lossy 재encode이므로 final decoded 값이 원본과byte 동일하다고 주장하지 않는다.

## 자동 검증·시작 조건

단위검증에서 native AAC1600 대응과 wrong/missing skip·frame·packet·padding, provider의edit/skip proof·timestamp·연속payload·동일file, 준비의 부분frame 증거, output의multi-packet skip/discard/frame을 검사했다. 마지막 `dotnet build ClipMaker.slnx --no-restore`는 경고/오류0이며 각project의 `dotnet test --no-restore --no-build`로 Core143/Media Infrastructure159/Downloader846, **총1148개 실패/skip0**을 확인했다. native작업이assembly를 사용하는 동안 build로 덮어쓰지 않았다.

[안전한 구조화 증거](evidence/PHASE_1_AAC_PRIMING_SUPPORT.json)와 기존 실패 기록을 보존했다. source ID/format/숫자·hash·로컬path만 포함하며 rawmetadataJSON/signed URI/인증값은 없다.

이번 요청은 CLI의 같은 입력으로 재확인할 수 있다. 보존final은 `.artifacts/aac-skip-support/e7b04578a5c647dc9b8cf9c363c14342/fast.mp4`와 `.artifacts/aac-skip-support/58b7bedefbe843fd9f0f0e43195b6dce/accurate.mp4`다. 영상/음성/동기를player로 직접 확인한 사실은 아직 없다. Phase1 전체Passed/Completion/Tag는 계속Pending이며 Phase2/WPF 다운로드UI/ClipEditor로 진행하지 않는다. source-start 예산 밖·미확인delayed audio/codec/edit/clock 등 기존 미지원 조건과 NativeEnd Unknown도 유지한다.
