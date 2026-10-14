# Phase 1 후속 — CHZZK 30초 Fast 요청의 제한 예산 보완

2026-10-14. [상한 진단](PHASE_1_CHZZK_FETCH_BUDGET_DIAGNOSIS.md)의 후속이다. 기존 CLI의 32 MiB / 30초는 출력 길이가 아니라 원본 참조 획득 예산이었다. CHZZK `14791589`의 best `hls-8384` / Fast / `00:46:10:00`~`00:46:40:00`은 실제 whole-reference 36초를 요구해 실패했다.

후속 [YouTube30초Fast 검증](PHASE_1_YOUTUBE_THIRTY_SECOND_SUPPORT.md)으로 현재 CLI 시간 예산은60초다. 아래64MiB/40초는 이 CHZZK 검증 시점의값이다.

## 제한 실험과 변경 근거

| 실행 | 예산 | 결과 |
|---|---|---|
| 기존 실제 CLI | 32 MiB / 30초 | 계획 단계 `HlsFetchedSpanBudget`, 목표 구간 미획득, final 없음 |
| Application 통합 실험 | 32 MiB / 40초 | 계획 통과 후 HLS 수집의 바이트 제한 실패, final 없음, owned 파일/잔존 0 |
| Application 통합 실험 | 64 MiB / 40초 | native/source 대응·Fast 처리·output 검사·최종 확정 성공, owned 파일/잔존 0 |

성공 실험 중 소유 source 파일을 읽기 관찰했다. 목표 참조는 36,787,855 bytes, 시작 참조 두 파일은 각 2,220,785 bytes였다. 목표 파일 하나만으로도 기존 33,554,432 bytes 상한을 초과한다. 세 파일 합계는 41,229,425 bytes이며 playlist 수집 비용은 별도로 budget에 포함된다. 이 합계를 정확한 총 network 비용으로 표시하지 않는다.

CLI 기본 획득 예산을 **64 MiB / 40초**로 조정했다. 실제 필요한 36초를 허용하고 관찰한 파일 비용·누적 조회 비용을 담는 제한값이다. 전체 영상 획득, 사용자 설정 옵션, 설치/패키지 변경은 없다. 단일 HTTP read·프로세스 JSON 상한 32 MiB 등 별도 상한은 바꾸지 않았다. 요청 앞뒤 참조 여유·whole segment 선택, native sample/packet·source 축·payload/picture·output gate, 확인·취소·정리·덮어쓰기 방지는 그대로 적용된다.

## 실제 결과

Application 성공 job `7ee0156b-62db-4eb9-b545-f4210831779a`의 최종 파일은 `.artifacts/chzzk-budget-experiment/60dca02e316947198c3437cc8c0376db/clip.mp4`다. SHA256은 `38ce8173b4574f269161f5e7fe7967798148e648cfec9f565562a895f87b3974`다. 별도 FFmpeg 전체 영상·음성 decode도 exit 0으로 통과했다.

요청 사용자 범위는 `[2770,2800)`이고 Fast 실제 영상은 `[26582413/9600,26880013/9600)`, 음성은 `[26582413/9600,2800)`이다. 실제 시작은 약 46:09.001354, 영상 끝은 약 46:40.001354다. 기존 Fast 정책대로 요청과 차이를 안내한다. AAC skip 961 / tail 0, audio zero 삽입·중복 제거 0이며 SourceEndVerified=false다.

조정한 기본 예산의 실제 CLI에서도 동일 입력·best/Fast·`--yes`로 exit 0을 확인했다. job은 `1f8c64de-ff91-4f60-a6fe-1d5f182146b4`, final은 `.artifacts/chzzk-span-cli/caf35a9dd92a412fa466e056aa6a19ec/fast.mp4`, 31,798,101 bytes다. SHA256은 위 Application 실험 출력과 동일해 별도 전체 decode를 통과한 파일과 byte identity가 같다. CLI가 같은 요청/실제 표시 범위·skip·tail을 안내한 것을 확인했다. Application 실패/성공 및 실제 CLI의 세 job 접두사 작업 폴더 수는 모두 0이고 CLI 출력 폴더에 staging은 없다.

영속 관찰은 [JSON 증거](evidence/PHASE_1_CHZZK_FETCH_BUDGET_SUPPORT.json)를 따른다.

## 검증·지원 범위

솔루션 build 경고/오류 0, Downloader Infrastructure 848개 실패/skip 0을 새 기본값에서 재확인했다. Core/Media suite는 이 후속에서 재실행하지 않았으며 이전 143/159 통과 기록을 그대로 구분한다.

이번 지원은 위 원본·선택 포맷·구간의 **Fast**다. 모든 30초 클립 지원을 보증하지 않는다. segment 길이/경계·비트레이트·source 지원 여부에 따라 64 MiB / 40초를 초과하거나 다른 gate에서 실패할 수 있다. 46분 지점의 Accurate는 기존 source-start prefix 전략상 예산 밖이며 성공으로 표시하지 않는다. 직접 player 영상/음성·동기 확인과 Phase 1 전체 Passed/Completion/Tag는 Pending이다.

사용자 `out.mp4`는 조작하지 않았고 소유한 고유 ignored 출력만 사용했다. 일반 정책 재승인·Phase 2·WPF 다운로드 UI·ClipEditor 변경은 없다.
