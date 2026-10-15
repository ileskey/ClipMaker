# Phase 1 G2 — 선택 포맷 원점·획득 mapping·coverage

실행일: 2026-10-11 (Asia/Seoul). branch `phase/1`, 시작 HEAD `6d8687245de1f0a9246e0d0b2e54601a0562d99d`, 작업 트리 clean. 이전 시간 의미 채택은 `8b8cd05`, G1 기록은 `6d86872`에 커밋되어 있었다. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **한 CHZZK source·두 선택 포맷에서 새 원점 확인, 실제 획득 packet mapping·decoded presentation coverage 대조 완료**. 사용자 시간 의미는 채택한 선택 포맷 기준이다. 제품 획득 서비스·범용 원점 측정·G3 audio 값/끝 경계·G4 Fast/다른 source·전체 4단계 준비와 Phase 완료는 별도다.

## 사전 범위·실제 수행

[G1 결과](PHASE_1_G1_ORIGIN_SEEK_RESULT.md)와 [시간 하위 계약](PHASE_1_SOURCE_TIMELINE_CONTRACT_DRAFT.md)을 사용했다. 설치된 yt-dlp 2026.08.19·FFmpeg/ffprobe 8.1·SDK 10.0.302를 그대로 사용했다. 제품 코드·솔루션·패키지는 변경하지 않았다.

기존 CHZZK video `15430558`의 hls-8384(1080p60), hls-3192(720p60)를 fresh metadata로 재확인했다. Completed·duration 2667초였다. 각 포맷에 대해 다음 순서로 수행했다.

1. media playlist와 init·첫 segment를 새로 읽어 stream start_pts/time_base와 첫 decoded video/audio PTS의 일치를 확인했다. 원시 playlist·서명 URL·header는 메모리에서만 사용했다.
2. 원격 source reference를 `90%106`의 제한된 시각 창에서 packet hash·PTS와 decoded frame PTS/sample 수로 읽었다. 원격 demuxer 시작과 stream 시작은 이번 초기 파일의 값과 같았다.
3. 사용자 target `[91.25,101.25)` 주변의 `[89.25,103.25)`를 선택 포맷 시간으로 잡고, `Q=O+U−F`를 적용해 yt-dlp 구간을 **각각 한 번** 획득했다. 이 2초 여유 폭은 이번 사례의 trial 값이다.
4. 로컬 packet hash/PTS로 stream별 Bᵢ를 새로 구하고 target packet과 decoded 영상·음성 presentation 구간을 대조했다. 원본 필요 packet 누락·잘못된 offset은 오프라인 목록 대조로 검증했다.

HTTP playlist/init 각각 2MiB, 초기 media 16MiB, HTTP 각 읽기와 probe 60초 제한을 유지했다. 원격 reference stdout은 기존 32MiB 한도에서 complete/untruncated였다. 재시도는 기존 yt-dlp 단위별 2회 추가·fragment 누락 금지 정책이며 Application 전체 재시도는 없다. 초기 playlist는 ENDLIST·media sequence 1·EXT-X-MAP을 확인했다. 실험 범위에서 암호화/byte range 입력은 진행하지 않는 harness였다.

HLS 입력의 `ffmpeg_i1:-extension_picky 0`은 기존 검증된 입력 한정 실험 옵션을 그대로 썼다. 기본 input 상대 seek·stream copy로 획득했으며, G1에서 반례가 나온 copyts 절대 accurate-seek 경로를 추가하지 않았다. 전체 VOD 다운로드·화질/모드 fallback·도구 설치·cookie import·출력 trim/재인코딩은 수행하지 않았다.

## 1. 새 원점과 실제 seek 인자

| 항목 | 1080p | 720p |
|---|---:|---:|
| 확인한 선택 원점 O | 95935/48000초 | 99007/48000초 |
| 확인한 remote demux 시작 F | 1998646/1000000초 | 2062646/1000000초 |
| F−O | 1/6000000초 | 1/6000000초 |
| target source IN | 4475935/48000초 | 4479007/48000초 |
| target source OUT | 4955935/48000초 | 4959007/48000초 |

첫 decoded video/audio PTS는 각 stream start_pts와 일치했다. 초기 파일·playlist의 hash는 이전 실험과도 같았지만, 이번에는 값과 decoded 시작을 새로 확인했다. 과거 보정 상수를 그대로 전제하지 않았다.

두 포맷의 획득 seek 후보는 정확한 유리수로 IN `535499999/6000000`, OUT `619499999/6000000`초다. harness는 decimal로 표현해 section 인자를 만들었고 yt-dlp가 실제 전달한 FFmpeg `-ss`는 **`89.24999983333333`**, `-t`는 **`14.0`**이었다. copyts·seek_timestamp는 없었다. 부동소수 인자 표현과 integer PTS target 계산을 구별한다.

verbose command에는 header 개행이 있어 최초 첫-line-only 파서가 ss/t를 빈 문자열로 기록했다. 기존 안전 로그의 전체 명령 범위를 검사해 보완했으며 추가 원격 요청은 하지 않았다. actual command를 확인하기 전 최초 파서의 false/빈 값으로 정책을 판단하지 않았다.

새 prefix 파일은 이전 일반화 실험의 `-ss 89.25` 획득 파일과 두 품질 모두 SHA256까지 같았다. 이번 변환 인자 차이는 이 사례의 획득 결과를 바꾸지 않았다. 이것으로 FFmpeg 내부의 microsecond 파싱 값을 직접 관찰했다거나 모든 경계에서 고정 오차를 허용한다고 선언하지 않는다. 실제 파일의 source/local 관계를 별도 증거로 판정한다.

## 2. 새 stream mapping·packet coverage

공통 clock 48000은 실제 측정한 source/local time_base의 공통 분모에서 얻었다. 표의 Bᵢ는 `S=Lᵢ+Bᵢ` 관계이며 단위는 이 clock이다.

| 항목 | 1080p | 720p |
|---|---:|---:|
| video Bᵢ | 4380000 | 4383192 |
| audio Bᵢ | 4379935 | 4383007 |
| unique video anchor | 855 | 854 |
| unique audio anchor | 668 | 657 |
| 필요/확보 target video packet | 600/600 | 600/600 |
| 필요/확보 target audio packet | 470/470 | 470/470 |
| target과 겹치는 미확인 packet | 0 | 0 |
| ambiguous/unresolved 대응 | 0 | 0 |
| prefix 파일 bytes | 15620453 | 5931219 |

각 stream의 unique anchor들은 한 개의 정확한 offset에 일치했다. source와 local의 같은 payload·PTS 대응으로 구했으며, 요청 section 시작이나 prefix start_time을 Bᵢ로 채택하지 않았다. 영상·음성 offset을 각각 지워 동기를 바꾸지 않았다.

720p local audio 10 packet은 remote reference에서 같은 payload 대응을 확인하지 못했다. predicted source interval은 모두 target 밖이고 IN overlap도 없음을 확인했다. 이들을 “변경된 packet”이나 “문제없음”으로 단정하지 않고 상세 증거에 hash/PTS·범위와 함께 남겼다. 해당 사항을 target의 확인된 470 packet·decoded interval coverage와 구별한다.

## 3. decoded frame·음성 sample 구간

source reference와 local을 실제 decode해 얻은 presentation 시각을 새 Bᵢ로 비교했다. video는 target 내부의 600개 PTS 집합이 정확히 같았다. audio는 target과 겹치는 decoded 구간들을 clip한 목록과 union이 각각 정확히 같았다. AAC 값의 byte 동일성·실제 최종 출력은 이번에 검사하지 않았다.

| audio clock 48000 | 1080p source/local | 720p source/local |
|---|---:|---:|
| 겹침을 포함한 선택 sample 구간 길이 합 | 479999/479999 | 480001/480001 |
| 중복을 제거한 union 길이 | 479998/479998 | 479999/479999 |
| 480000 중 source gap 길이 | 2 samples | 1 sample |
| 중복 포함 합과 union 차이 | 1 sample | 2 samples |

이는 획득 파일에 **원본에 실제 존재하는 target presentation 구간이 보존**되었다는 판정이다. 원본 자체의 gap/overlap을 채워 완전한 연속 480000 sample을 만들었다는 의미가 아니다. G1의 AAC seek PCM 값 차이, priming/padding·유효 presentation 끝과 함께 G3에서 처리 정책을 검증해야 한다. sample 수 차이를 임의 silence·duplicate 제거·global PTS 이동으로 숨기지 않았다.

## 4. 오프라인 반대 대조

실제 파일을 손상시키는 대신 확인된 local 대응 목록에서 필요한 항목을 제외하거나 offset을 바꿔 비교했다. 추가 다운로드·인코딩은 없다.

| 대조 | 두 포맷 결과 | 기대 판정 |
|---|---|---|
| target video packet 하나 제외 | 599/600 coverage | 필수 coverage 실패 |
| target audio packet 하나 제외 | 469/470 coverage | 필수 coverage 실패 |
| audio Bᵢ를 +1024 samples 변경 | 같은 payload/PTS 대응 anchor 0 | mapping 확인 실패 |

각 대조가 원래 required count/일관된 anchor 조건을 충족하지 못함을 확인했다. 이것은 제품 서비스의 실패 통합 테스트가 아니라 실제 자료에 대한 검증 조건 대조다. 제품의 오류 반환·staging 미확정 수명은 후속 구현과 통합 검증 대상이다.

## 실제 검증·다음 단위·커밋

최종 harness 빌드는 경고/오류 0, 기록된 probe/획득 **10개 호출 모두 exit 0·root/descendant 종료·reader drain·완전한 stdout**을 확인했다. 별도로 기존 metadata 서비스를 통한 조회는 성공했다. 새 원점 확인·integer offset·필수 packet·decoded frame/audio interval·오프라인 반대 대조가 두 사례에서 기준을 충족했다. helper의 IN-overlap audio 검사까지 보완하고 로컬 대조를 재수행했으며 원격 요청은 반복하지 않았다. 제품 코드가 바뀌지 않아 전체 제품 테스트는 반복하지 않았다.

- [사전 기준·fresh origin·seek 인자·coverage·실행 scalar](evidence/PHASE_1_G2_COVERAGE.json)
- [source/local packet·decoded frame·target payload 대응](evidence/PHASE_1_G2_PACKET_FRAME_COVERAGE.json)

증거에는 서명 URL·header·cookie·원시 metadata/playlist를 포함하지 않는다. 미디어·안전 로그·harness는 ignored 실험 폴더에 보존했다. 자료의 source 범위와 미확인 target 밖 packet도 숨기지 않는다.

다음은 **G3 audio/sample·끝 경계와 Accurate 결과 조건 검증**이다. G1의 최대 1 LSB 값 차이를 허용치로 채택하지 않았으며, 이번 gap/overlap은 source의 presentation 특성과 획득 누락을 분리하는 근거다. 기존 6단계와 4단계 내 논리적 커밋 순서는 유지한다. 다른 source·분리 A/V·Fast 경로(G4)와 최종 계약 통합(G5)은 남아 있다.

이번 커밋 범위는 결과 문서·evidence 두 개·하위 계약안·상세 설계·baseline **6개 파일**이다. staging/commit·Phase 2·WPF 다운로드 UI·ClipEditor·제품 서비스 구현은 수행하지 않았다.
