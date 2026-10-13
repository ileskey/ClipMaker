# Phase 1 4a-3c-3b 로컬 FFprobe와 byte identity

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `ace90b31b2e5748b4dfddf69bc1e8c2d604c5df5`, clean. 직전 4a-3c-3a 커밋을 확인했다. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **수집 참조의 로컬 FFprobe 실행·파일 identity 연결 구현/관련 검증 완료**. [관찰 파서](PHASE_1_STAGE_4A3C3A_OBSERVATIONS_RESULT.md)에 internal `InitialReferenceProbe`를 연결했다. source inspector/native 원점/coverage 판정과 실제 acquisition 전체는 아직 아니다.

## 구현 범위

- collector의 reference를 받아 기존 파일을 Open/read-only/FileShare.Read로 열고 실제 길이와 SHA-256을 실행 전후 검사한다. Windows에서는 열린 동안 write/delete 공유를 허용하지 않는다. 수집 파일/playlist ID·format provenance·byte budget/range를 가진 원래 reference와 probe version·Demux 관찰을 함께 반환한다. hash 비교는 대소문자를 구분하지 않는다.
- byte 수/hash 불일치·파일 I/O/접근 실패는 VerificationFailed/Data=null이다. 파일을 생성·변경·삭제하거나 실패/취소에서 collector 소유 파일을 정리하지 않는다. 소유 수명/잔존 자원은 호출 adapter 책임으로 남는다. hash는 local bytes 동일성 검사이며 원격 source 동일성/native 검증 증거가 아니다.
- locator/settings에서 FFprobe를 찾고 version 실행15초·미디어 probe60초를 적용한다. local absolute path만 전달하며 protocol whitelist=file, format whitelist=mov로 제한한다. selected JSON 필드만 stdout Full로 받고 stderr Tail은 오류 메시지에 노출하지 않는다. network locator/쿠키/HTTP header/다른 포맷 fallback/재시도는 없다.
- 기존 ProcessRunner의 bounded Full 출력/종료 확인을 재사용한다. 파서의 수락 상한은8MiB/100,000 frames이며 runner의 수집 상한을8MiB로 바꾼 것은 아니다. root/descendant/drain 확인 실패는 ProcessTerminationFailed가 Cancelled/Timeout보다 우선한다. confirmed 취소·timeout·nonzero/실행 실패·출력 상한·EOF/complete/truncation을 분리하고 실패에는 관찰을 발행하지 않는다.
- 성공 결과도 Demux 관찰이다. decoded frame 목록·빈 목록·선언/평균 FPS가 native O/CFR/decoder 준비/coverage 완료를 뜻하지 않는다. stderr를 버리지 않으려 raw 내용을 public에 노출하는 대신, native/decoder completeness 검증을 후속 단계에 남긴다. 이 관찰만으로 Complete/Verified fact를 생성하지 않는다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| Downloader Infrastructure tests `--no-restore` | 최종 exit0, 통과416/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |
| 기존 G2 initial.bin에 실제 FFprobe8.1로 동일 제한 옵션 실행 | exit0, video/audio2 streams·213 decoded frames |

새12개 테스트는 tiny 실제 fixture 파일과 fake runner/locator로 prehash 길이/내용 불일치 차단, write lock, local 명령·version·시간 제한·원래 identity 보존, missing/version/process/termination/output 실패, 응답 직후/실행 전 취소·파일 보존을 검사한다. 첫 실행은 test의 ReadOnlyMemory 초기화 구문3곳에서 컴파일 오류가 났다. Array.Empty<byte>()로 고친 뒤416개가 통과했다.

별도 실제 CLI 검사는 기존 ignored `.artifacts/phase1-g2-coverage-20261011-01/c1080-fractional/initial.bin` 2,050,886bytes를 읽기만 했다. video time_base1/6000·start_pts/first pts12000·r/avg60/1, audio time_base1/48000·start_pts/first pts95935·r/avg0/0을 관찰했다. **이는 실제 옵션/출력 schema 대조이며 새 adapter의 real ProcessRunner end-to-end 실행 검증은 아니다.** 새 다운로드·설치·파일 변경은 없다. 이 한 파일의 결과를 사이트 전체/native 지원으로 일반화하지 않는다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

## 다음 단위·커밋

다음 **4a-3c-3c**는 codec/container edit·priming 및 native timestamp 관계를 확인하는 증거 경계다. 실제 source inspector에 원점/FPS fact를 연결하기 전에 무엇을 native로 확인할 수 있는지 검증해야 한다. playlist0/start_pts/최초 decoded PTS를 source O로 자동 채택하지 않는다. selected inspection/target acquisition·4a 전체와 Phase 전체는 미완료다.

이번 커밋 범위는 Infrastructure1·test1·결과/설계/source 계약/baseline4문서다. staging/commit은 하지 않았다. 권장 메시지: `feat: Probe owned initial references with byte identity checks`.
