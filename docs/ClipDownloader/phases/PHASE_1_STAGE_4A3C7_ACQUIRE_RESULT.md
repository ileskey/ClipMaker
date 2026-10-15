# Phase 1 4a-3c-7 HLS target 획득 adapter

실행일: 2026-10-14 (Asia/Seoul). 시작 HEAD `336b869789b7b4a1db9180d49a6bd46834ca06e6`, `phase/1`, clean. 중단 시점의 acquire 변경을 보존·수정했다. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **single HLS target AcquireAsync·source 재확인·scoped mapping/coverage 구현·관련 검증 완료**. [public inspection](PHASE_1_STAGE_4A3C6_INSPECTION_RESULT.md) 다음 단위다. YouTube indexed-source/4a 전체·qualification/처리/최종 clip 완료는 아니다.

SelectedSourceAcquisitionService에 IClipDownloadService를 연결했다. 같은 instance가 발행한 정확한 inspection object/cache와 job binding을 요구하며 SourceTimeline 미확인을 자동 승격하지 않는다. 원본 metadata/locator를 다시 확인하고 first reference를 다시 수집해 전체 hash가 inspection 때와 같은지 대조한다. known first bytes/config/clock 변화는 실패한다. 이는 관찰하지 않은 전체 원격 asset 변경을 증명하는 version ID가 아니다.

- user IN/OUT을 confirmed O로 native S에 옮기고 video first native start로 playlist 참조 후보를 계산한다. 첫 segment 길이만큼 이전/이후 참조를 포함하며0/playlist 끝으로 제한한다. 이 후보 계산만으로 성공하지 않고 실제 native timing/clock/coverage를 다시 검사한다.
- inspection의 source budget을 initial+refresh+target 전체 body 누적에 적용한다. target whole-reference span/최대64 segments도 검사한다. budget/전략 상한 밖이면 UnsupportedFormat이며 valid 사용자 구간을 Input InvalidRange로 바꾸지 않는다. fragment skip/full-video/mode/quality fallback은 없다.
- 같은 initialization+whole target fragments를 CreateNew 파일에 수집하고, 동일 hash/probe/native gate를 적용한다. 모든 초기 stream의 track/config fingerprint가 같고 실제 video native span이 playlist range의 같은 translation과 일치해야 한다.
- requested native range가 모든 필요한 stream의 관찰 support 안에 있어야 한다. source 첫 참조에서 확인된 leading empty만 명시적으로 인정한다. 원본 gap/overlap을 이 단계에서 채우거나 재타임하지 않는다. 전체 tail/복합 native support를 확인하지 못하면 성공을 강제하지 않는다.
- 원본 fragments를 변형하지 않은 무보정 전략의 source/local offset B=0과 requested native range의 Frames/Samples Complete scope를 AcquiredMedia에 반환한다. transfer/mapping/원본 sample 포함 대조이며 PCM 값/decoder preparation·출력 정확도·global CFR·final clip 성공과 다르다. qualification은4b/4c에 남는다.
- failed/cancelled Data=null, source resource registry는 initial/refresh/target의 소유 파일을 유지한다. 새 파일은 probe 전에 기록하고 collection cleanup 잔존 파일/종료 미확인을 인계한다. 실패 종료 시 NeedsCleanup을 표시하며 삭제하지 않는다. progress는 안전한 메시지/수치만 전달한다.

## 실제 검증

|실행|결과|
|---|---|
|Downloader Infrastructure tests `--no-restore`|최종 exit0,563 통과/0 실패/0 skip|
|`dotnet build ClipMaker.slnx --no-restore`|exit0,경고0/오류0|
|실제 CHZZK15430558 hls-8384·Fast request10~12초|Inspect/Acquire Succeeded·video/audio offset0·scoped required range115187/9600→134387/9600|

새8개 사례는 source first hash 변화·byte/span budget·refresh 취소/termination·pre-cancel·unrequested timeline·다른 service의 inspection 차단과resource 보존을 검사했다. 첫 빌드는 SignedMediaTime에 없는 비교 연산자를 사용해 CS0019 6곳에서 실패했다. 기존 CompareTo API로 수정한555개와추가 후563개가 통과했다. 최종 빌드 경고/오류0이다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

실제 public API 실행은 caller-created ignored `.artifacts/source-service-real/4d62e8ca5a6743d78abdb118d141790d`에 보존했다. old init2,050,886bytes·refresh2,050,886bytes·target8,201,514bytes, safe inspection/acquisition summaries338/785bytes다. target 참조는 필요한 이전/이후 whole references이고 최종10~12초 출력 파일을 만든 것은 아니다. source budget32MiB/span8초는 harness 요청값이며 제품에서 모든 길이를 지원한다는 의미가 아니다. harness3분 cancellation은 실험용이다. 사용자 승인된 bounded 다운로드만 했고 설치/cookies/remote components·기존 사용자 파일 변경은 없다.

## 다음·커밋

다음은 indexed source의 별도 init/SIDX byte-range 경로·movie/native bridge와분리 A/V 관계, 양 사이트 source 변경/HTTP/취소/resource 통합검증이다. 현재 HLS scope 밖의 관계를 추정해4a 완료로 표시하지 않는다. Phase2/WPF 다운로드 UI/ClipEditor와4b/4c 구현은 진행하지 않는다.

범위: Infrastructure3·tests2·결과/설계/source 계약/baseline4문서. 메시지: `feat: Acquire bounded HLS targets with scoped native mappings`.
