# ClipDownloader Product Requirements

## 1. 프로젝트 개요

프로젝트명(가칭): **ClipDownloader**

ClipDownloader는 온라인 VOD 또는 로컬 동영상에서 사용자가 원하는 구간만 지정하여 클립 파일로 추출하는 Windows 데스크톱 애플리케이션이다.

초기 공식 지원 대상은 다음 두 플랫폼이다.

- CHZZK VOD
    - `https://chzzk.naver.com/video/{number}`
- YouTube
    - `https://www.youtube.com/watch?v={id}`
    - `https://youtu.be/{id}`

향후 Twitch VOD, Vimeo 등 다른 yt-dlp 지원 사이트로 확장할 수 있어야 하지만, MVP에서는 CHZZK와 YouTube만 공식 지원 범위로 본다.

이 프로젝트의 최종 목표는 단순 다운로더가 아니라 향후 제작할 WPF 기반 키리누키 편집기의 **Media Import + Clip Extraction 모듈**로 재사용 가능한 구조를 만드는 것이다.

---

## 2. 핵심 사용자 시나리오

사용자는 다음 흐름으로 클립을 생성할 수 있어야 한다.

1. CHZZK 또는 YouTube URL을 입력한다.
2. 프로그램이 영상 정보를 불러온다.
3. 시작 구간(IN)을 `HH:MM:SS:FF` 형식으로 지정한다.
4. 종료 구간(OUT)을 `HH:MM:SS:FF` 형식으로 지정한다.
5. 원하는 화질과 출력 옵션을 선택한다.
6. 필요한 구간만 다운로드/추출한다.
7. 최종 MP4 클립을 지정 폴더에 저장한다.

향후 Preview Player가 구현되면 사용자가 직접 타임코드를 입력하지 않고 영상 재생 위치에서 IN/OUT을 지정할 수 있어야 한다.

---

## 3. 기능 요구사항

### 3.1 URL 입력

프로그램은 입력 URL의 플랫폼을 자동으로 판별해야 한다.

사용자가 CHZZK / YouTube 버튼을 별도로 선택할 필요는 없다.

지원 대상:

- CHZZK VOD URL
- YouTube 일반 영상 URL
- YouTube 단축 URL

지원하지 않는 URL은 명확한 오류 메시지를 표시한다.

---

### 3.2 영상 정보 조회

URL 입력 후 다음 정보를 불러올 수 있어야 한다.

필수:

- 영상 제목
- 총 재생 시간
- FPS
- 사용 가능한 해상도
- 업로더 또는 채널/스트리머명

선택:

- 썸네일
- 업로드 날짜
- 영상 ID

영상 정보 조회는 가능한 한 yt-dlp를 활용한다.

---

### 3.3 타임코드

사용자는 시작/종료 지점을 다음 형식으로 입력할 수 있어야 한다.

`HH:MM:SS:FF`

예:

`01:32:17:13`

여기서 FF는 frame index이다.

타임코드 변환에는 해당 영상의 실제 FPS를 사용해야 한다.

예:

30fps 영상에서

`00:00:10:15`

는

`10.5초`

를 의미한다.

타임코드 처리는 별도 타입으로 구현한다.

예시 개념:

```csharp
Timecode
{
    Hours
    Minutes
    Seconds
    Frames
    FrameRate
}
```

검증 조건:

- Start >= 0
- End > Start
- End <= 영상 전체 길이
- Frames < FrameRate

---

### 3.4 구간 추출

프로그램은 전체 원본 영상을 반드시 먼저 다운로드하지 않아도 된다.

가능하면 원격 스트림에서 필요한 구간만 받아 처리한다.

사용자는 두 가지 추출 모드 중 선택할 수 있다.

#### Fast Mode

목표:

- 빠른 처리
- 재인코딩 최소화
- 원본 화질 유지

특징:

- FFmpeg stream copy 사용 가능
- 시작 위치가 keyframe 제약으로 인해 몇 프레임 정도 어긋날 수 있음

#### Accurate Mode

목표:

- 가능한 정확한 IN/OUT 지점
- 프레임 단위 정확도

특징:

- 필요한 경우 재인코딩
- Fast Mode보다 느릴 수 있음

MVP에서는 먼저 안정적인 구간 추출을 우선하며, 두 모드의 내부 최적화는 후속 단계에서 개선할 수 있다.

---

### 3.5 화질 선택

사용자는 다음과 같은 방식으로 출력 화질을 선택할 수 있어야 한다.

예:

- Source / Best
- 1080p
- 720p
- 480p

실제 제공되는 화질 목록은 영상에 따라 달라질 수 있다.

YouTube의 경우 video stream과 audio stream이 분리되어 있을 수 있으므로 yt-dlp + FFmpeg를 통해 병합해야 한다.

---

### 3.6 출력

초기 출력 포맷:

- MP4

사용자가 지정할 수 있는 항목:

- 저장 폴더
- 파일명

기본 파일명 예시:

`{title}_{start}_{end}.mp4`

파일명에 사용할 수 없는 Windows 문자는 자동 정리한다.

---

### 3.7 여러 구간 처리

하나의 영상에서 여러 구간을 등록할 수 있어야 한다.

예:

- 00:21:13:04 → 00:22:41:18
- 01:07:21:13 → 01:09:04:02
- 03:54:11:51 → 03:55:20:07

각 구간은 다음 정보를 가진다.

```csharp
ClipSegment
{
    Start
    End
    Name
}
```

향후 UI에서는 구간 Queue를 제공한다.

기능:

- 구간 추가
- 구간 삭제
- 순서 변경
- 개별 다운로드
- 전체 다운로드

---

### 3.8 진행률 및 작업 제어

다운로드/추출 과정에서 다음을 표시해야 한다.

- 현재 작업 상태
- 진행률
- 현재 처리 중인 구간
- 오류 메시지

사용자는 실행 중인 작업을 취소할 수 있어야 한다.

CancellationToken 기반 설계를 권장한다.

---

## 4. 지원 범위

### 공식 지원

- CHZZK VOD
- YouTube

### 실험적 지원

yt-dlp가 지원하는 기타 URL은 향후 Experimental 기능으로 허용할 수 있다.

단, MVP에서는 기타 사이트 지원을 제품 요구사항으로 보지 않는다.

---

## 5. 향후 기능

### Preview Player

기능:

- 재생 / 일시정지
- Seek
- 현재 시간 표시
- frame step
- IN 지정
- OUT 지정
- Timeline

권장 단축키:

- Space: 재생/일시정지
- Left / Right: Seek
- `,` / `.`: 이전/다음 frame
- I: IN 지정
- O: OUT 지정
- Ctrl+D: 다운로드

---

### Browser Integration

향후 Chrome Extension을 제공할 수 있다.

기능 예:

CHZZK / YouTube 페이지에서

`ClipDownloader에서 열기`

버튼 제공.

Native Messaging을 필수로 하지 않고, Custom URI Scheme을 우선 검토한다.

예:

`ClipDownloader://open?url=...`

추후 현재 재생 위치도 함께 넘길 수 있다.

---

### Local File Support

최종적으로 온라인 영상뿐 아니라 로컬 파일도 동일한 Clip Processor를 이용할 수 있어야 한다.

예:

```text
Online Source
    ↓
Downloader
    ↓
Clip Processor
    ↓
Output

Local MP4
    ↓
Clip Processor
    ↓
Output
```

---

## 6. 비기능 요구사항

- Windows 우선 지원
- C# / .NET
- WPF UI
- async/await 적극 사용
- UI Thread block 금지
- 외부 프로세스 실행 중 UI freeze 금지
- yt-dlp와 FFmpeg 경로 관리 필요
- 작업 취소 가능
- 오류 메시지는 사용자에게 이해 가능한 형태로 변환
- 사이트별 로직과 UI를 강하게 결합하지 말 것
- 향후 테스트 가능한 구조를 유지할 것

---

## 7. 법적/운영 전제

이 애플리케이션은 사용자가 다운로드 및 편집 권한을 가진 영상 또는 해당 플랫폼의 이용 조건상 허용되는 범위에서 사용하는 것을 전제로 한다.

DRM 우회 기능은 범위에 포함하지 않는다.
