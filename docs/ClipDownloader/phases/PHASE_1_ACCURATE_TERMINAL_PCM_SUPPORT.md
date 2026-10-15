# Phase 1 후속 — Accurate 끝 AAC PCM 준비·재구성 지원

2026-10-14. 시작 HEAD `faf6798`, branch `phase/1`에서 사용자 `wFOjWCLPDDM` / best137+140 / 25fps / Accurate / IN10초·OUT40초의 `SourceDecodedFrameLayout` 실패를 확인했다. 미추적 `22.mp4`, `out.mp4`를 보존했고 사용자 `out2.mp4`도 조작하지 않았다. 실제 출력·진단은 고유 ignored 경로를 사용했다.

## 원인과 제한된 변경

[이전 Fast 지원](PHASE_1_YOUTUBE_THIRTY_SECOND_SUPPORT.md)에서 원본 마지막 AAC의 native/packet/frame duration384와 decoded SampleCount1024, 단일 edit의 정확한 표시 끝을 검증해 `TrailingDecodedPaddingSamples=640`을 보존했다. Accurate 준비는 모든 frame의 duration=decoded count를 요구해 이 마지막 frame을 거절했다. 준비를 통과시키기만 해도 기존 재구성 계획은 decoded 전체 길이와 유효 support 끝이 일치하지 않아 실패하는 구조였다.

준비 단계는 검증된 tail0~1023만 마지막 frame의 duration/count 차이에 적용한다. 다른 frame은 기존 duration/count·leading skip·format/rate/channels·PTS 조건을 유지한다. 누락/잘못된 proof, 중간 short frame은 거절한다. 실제 source-start prefix pre/post identity와 **전체** FFmpeg decode·바이트 수/hash 검사는 유지한다.

captured PCM은2098624 samples,16788992 bytes(stereo float32) 전체를 그대로 보존한다. valid support는2097984 samples이며 마지막640 decoded values가 support 밖이다. 재구성 계획에 명시적인 trailing decoded padding 수를 추가해 captured 전체 길이와 valid placement 길이를 각각 검사한다. 유효 마지막 frame placement는384 samples만 사용하고 실제 decoded 파일의 offset/count는 전체1024를 기준으로 유지한다. decoded file을 잘라 맞추거나 원본 값을 수정하지 않는다. gap zero/first-wins와 selected sample-grid 계약은 유지한다.

CLI의 현재64MiB/60초 예산·확인·취소·정리·덮어쓰기·native/codec/picture/output gate는 바꾸지 않았다. SourceEndVerified=false를 유지하며 이 관찰을 모든 원격 입력/끝 padding 지원으로 확대하지 않는다.

## 실제 Accurate 결과

같은 사용자 입력·best/Accurate·`--yes`로 실제 CLI exit0/final 확정을 확인했다. job `8634eac5-0ba1-4987-8f05-ddd335e45e22`, output `.artifacts/youtube-thirty-cli/6551072b3f2343a98c1cfac1fe5a95c8/accurate.mp4`,2122711 bytes·SHA256 `315bfdd4da60e108a8ba0396b8a2cf18bec55a16e319552335c2d9787316097b`다.

요청·실제 video·실제 audio는 모두 `[10,40)`초다. zero 삽입/중복 제거0, **출력** AAC skip1024/tail8이다. **원본** decoded tail640과 출력 encoder tail8을 구별한다. 별도 최종 MP4 전체 A/V decode도 exit0이며 실제 job 접두사 폴더/staging은 각각0이다.

별도 media oracle은 `.artifacts/accurate-tail-oracle/de792f8084274282af3dfc06e24e12d7/`에 captured source/decoded/render/staging을 보존했다. media 처리·output gate 성공을 확인했으며 이 의도적인 증거 보존을 CLI cleanup 잔존으로 세지 않는다.

## 독립 lossless PCM 검증

같은 captured source audio를 별도 FFmpeg 명령으로 full decode했고 실행 전후 source hash가 같았다. 전체 decoded PCM16788992 bytes·SHA256 `60ebd5c4c1537b7ddd98315f9e18f97b698d620bed1ac0d76f0e39851c6cf56d`는 실제 준비 PCM과 일치했다.

독립 PCM의 `[441000,1764000)` samples를 바이트 위치로 잘라 hash했다. 이1323000 samples·10584000 bytes는 실제 render PCM과 같았으며 SHA256은 `ca57287b3914d71e69d57caa66b20ef61bf35319f66a64b6bc3f9e622a4311b2`다. 이것은 실제 captured decode와 lossless render의 값 대응이다. lossy AAC로 재encode한 final decoded PCM의 byte 동일성을 주장하지 않는다. 독립 증거는 `.artifacts/accurate-tail-pcm-check/e52ab2573c3548f7ad1b8ea65c23fb42/summary.json`이다.

## 회귀와 완료 상태

솔루션 build 경고/오류0, Core143 / Media Infrastructure168 / Downloader855개, 총1166개 실패/skip0을 확인했다. 새 테스트는 tail proof 누락/불일치·중간 frame 변경의 거절, mono/stereo에서 captured tail sentinel 값이 출력에 포함되지 않는 것, captured 값 보존과 정확한 valid support·전체 길이 관계를 검사한다.

영속 관찰은 [JSON 증거](evidence/PHASE_1_ACCURATE_TERMINAL_PCM_SUPPORT.json)를 따른다. 해당10~40초 Accurate 생성·sample 값 재구성 검증은 완료했으며 이전 미지원 설명은 당시 이력이다. 모든 사이트/구간/codec/tail을 지원하거나 직접 player 영상·음성·동기를 확인한 것으로 표시하지 않는다. 직접 player와 Phase1 전체 Passed/Completion/Tag는 Pending이며 Phase2/WPF 다운로드 UI/ClipEditor로 진입하지 않는다.
