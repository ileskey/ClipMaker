# Phase 1 4a-3a bounded source HTTP reader

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `1978748e0da300c55ed3c5e916300f3f498945fb`, branch `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **4a-3a HTTP byte reader 구현·관련 검증 완료**. [4a-2b](PHASE_1_STAGE_4A2B_RESULT.md)의 실제 adapter를 연결하기 위한 첫 logical commit이다. source locator 조회·manifest/index/native 해석·inspection/acquisition 서비스 구현·실제 사이트 다운로드·4a-3 전체/Phase 완료는 아니다.

## 구현한 transport 정책

Infrastructure.Downloads의 request/reader2파일과 테스트 접근용 AssemblyInfo1파일을 추가했다. request/result/reader는 internal이며 signed locator·요청 headers를 Application/Core 공개 snapshot에 넣지 않는다. 기존 진단/metadata·yt-dlp retry·모드/화질 정책은 그대로다.

| 명시 요청 | 허용 응답·완료 조건 |
|---|---|
| Resource | 200, Content-Range 없음, declared length가 있으면 정확히 일치. 없으면 bounded EOF. 작은 manifest/fragment용이며 전체 video fallback 기능이 아님 |
| HeaderRange | Range header, 정확한206 bytes start/end와 end보다 큰 known resource total. Content-Length가 있으면 요청 count와 같아야 함. 200을 full-response fallback으로 받지 않음 |
| QueryRange | 원래 query를 유지하고 range=start-end 추가, Range header 없음. 정확한206 또는 **정확한 Content-Length의200**만 허용. unknown-length200은 거절 |

어떤 모드도 response body 초과/부족·허용 밖 content encoding을 성공으로 받지 않는다. Range 요청의 base URI에 이미 range query가 있으면 혼합/중복을 거절한다. HeaderRange 실패 후 QueryRange나 Resource로 자동 변경하지 않는다. query-only200의 chunk 길이를 원본 resource total로 저장하지 않는다.

HTTP reader는 **요청당 자동 재시도0회**다. 429/503·연결/읽기 실패·range 오류도 같은 호출에서 재시도하지 않는다. 이는 초기 transport 정책이며 기존 yt-dlp 해당 단위2회 추가 retry를 바꾸지 않는다. 이후 source adapter의 재조회/locator 갱신은 source/선택 검증과 별도로 계획하고 전체 Application retry를 추가하지 않는다.

기본 handler는 redirect/cookie/자동 decompression을 끄고 HttpClient timeout은 Infinite로 구성한다. 원격 획득의 고정 timeout 없음·사용자 취소를 유지하며 서버/네트워크 실패는 안전한 transport failure로 반환한다. 검증용 주입 handler는 trusted dependency이며 source 입력으로 받지 않는다.

HTTPS locator는 user-info/fragment가 없어야 한다. headers는 User-Agent/Accept/Accept-Language/Referer/Origin을 명시 지원하고 snapshot 복사·중복/CRLF를 검사한다. Range/Accept-Encoding은 reader가 제어하며 Cookie/Authorization/Host 등을 받아 자동 인증하지 않는다. 실제 source가 요구하는 미지원 header는 후속 locator/adapter에서 근거를 확인해 검증해야 하며 조용히 추정/대체하지 않는다.

각 요청의 명시 max bytes를 적용하며 in-memory reader의 요청당 hard cap은32MiB다. 이는 **총 clip 크기/사용자 구간 제한이 아니다**. source strategy가 필요한 range/fragment를 나누고 inspection budget의 누적 bytes/span을 별도로 검사해야 한다. bounded body만 다루며 이 단위는 파일을 쓰거나 budget의 시간 범위/native coverage를 판정하지 않는다.

SendAsync(ResponseHeadersRead)·stream read·EOF에서 cancellation을 검사한다. 알려진 과대 length는 body를 읽기 전에 거절하고 길이가 없는 body도 상한+1 byte로 초과를 감지한다. 실패/취소는 Bytes=null이며 partial memory를 staging/미디어 snapshot으로 전달하지 않는다. request/response/stream은 dispose한다. 예외 원문·locator·header를 결과 message에 복사하지 않고 safe failure kind/status 숫자만 전달한다. Application 오류로 변환하는 책임은 source adapter에 남긴다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj --no-restore` | 최종 exit0, 통과327/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

새20개 HTTP 사례를 포함한 Downloader 테스트다. 정확한 range/query200/206, full/redirect/416/429/503 거절, body 미수신/초과·Content-Range 불일치, unknown length/encoding, bounded GET, 중간/EOF 취소·pre-cancel0호출, transport 예외의 private detail 차단과 request/header/query 불변식을 검사했다. 마지막 검토에서 base URI의 기존 range-query와 HeaderRange 혼합도 차단하고 테스트를 재실행했다. 또한 malformed Content-Length/Content-Range를 헤더 부재로 받아들이지 않도록 차단하고 최종327개를 검증했다.

테스트는 실제 HttpClient 요청/stream 처리 코드를 주입 HttpMessageHandler·비seekable body로 실행했다. **실제 네트워크/CDN/사이트 성공 검증은 아니다**. 작은 fake bytes만 사용하며 media 생성·다운로드·도구 설치·FFmpeg/ffprobe/yt-dlp 실행은 없었다. 기존 Core/Media.Infrastructure 전체 테스트를 이번 실행 결과로 합산하지 않는다. WPF는 솔루션 빌드만 확인했다.

## 다음 단위·커밋 경계

다음 **4a-3b**는 metadata 조회/선택을 다시 확인하면서 Infrastructure 내부의 locator·필수 HTTP header를 얻는 경계를 연결한다. public VideoInfo에 signed URL을 추가하거나 capture wrapper에 raw JSON을 기록하는 실험 방법을 제품에 복사하지 않는다. 이후 source별 bounded 초기/target 참조·manifest/index/native 해석·소유 파일/snapshot과 실제 inspection/acquisition adapter를 연결한다. 치지직과 YouTube의 실험 성공 범위를 먼저 적용하고 범위 밖 입력을 미지원/미확인으로 남긴다.

이후 실제 bounded 사이트 검증에서 header/HTTP 정책·identity·source/local mapping/coverage·취소/retry를 확인해야 한다. byte completeness가 native relation·decoder 준비·clip 정확도를 증명하는 것은 아니다. 전체 준비/Phase 완료를 선언하지 않는다.

이번 커밋은 Infrastructure3파일·test1파일·결과/설계/source 계약/baseline4문서다. staging/commit은 수행하지 않았고 사용자·기존 실험 자료를 보존했다. 권장 메시지: `feat: Add bounded source HTTP byte reads`.
