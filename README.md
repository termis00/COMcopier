# COMcopier

POS 시스템의 COM 포트 출력을 여러 프린터로 복사해주는 Windows 서비스입니다.

배달 플랫폼(쿠팡이츠, 배달의민족 등)별로 특정 프린터에만 출력하거나, 같은 주문서를 여러 매 인쇄하는 등 POS 프로그램 자체에서 지원하지 않는 유연한 출력 제어가 가능합니다.

## 동작 원리

```
POS / 배달앱 → COM10 ←가상쌍→ COM11 → COMcopier → COM3 (프린터A, 2매)
                                                  → COM4 (프린터B, 1매)
```

1. com0com 가상 COM 포트 쌍을 통해 POS 출력을 가로챔
2. 수신된 데이터에서 패턴(문자열)을 검사
3. 조건에 맞는 대상 프린터로 지정된 매수만큼 전송

## POS기 설치 가이드

### 준비물

- `deploy` 폴더 (압축 해제한 배포 패키지)

### 폴더 구성

| 파일 | 설명 |
|------|------|
| `COMcopier.exe` | 서비스 본체 |
| `COMcopier.ConfigUI.exe` | 설정 GUI 도구 |
| `appsettings.json` | 설정 파일 |
| `com0com-3.0.0.0-i386-and-x64-signed.zip` | 가상 COM 포트 드라이버 |
| `setup-virtual-ports.bat` | 가상 포트 설치 스크립트 |
| `install-service.bat` | 서비스 설치 스크립트 |
| `uninstall-service.bat` | 서비스 제거 스크립트 |

### Step 1. 폴더 배치

deploy 폴더를 POS기의 원하는 위치에 복사합니다.

```
예: C:\COMcopier\
```

### Step 2. 가상 COM 포트 설치

`setup-virtual-ports.bat`을 **우클릭 → 관리자 권한으로 실행**합니다.

- com0com 드라이버 설치 화면이 열리면 **기본 설정 그대로** 설치를 진행합니다.
  - `com# <-> com#` 컴포넌트는 **반드시 체크**되어 있어야 합니다.
- 설치 완료 후 스크립트가 자동으로 `COM10 ↔ COM11` 포트 쌍을 생성합니다.
- **PC를 재부팅**합니다. (가상 포트가 OS에 인식되려면 재부팅 필요)

> 다른 포트 번호를 사용하려면: `setup-virtual-ports.bat -PortA COM20 -PortB COM21`

### Step 3. POS 프로그램 출력 포트 변경

POS 또는 배달 접수 프로그램의 프린터 출력 포트를 가상 포트로 변경합니다.

```
변경 전: COM3 (물리 프린터에 직접 출력)
변경 후: COM10 (가상 포트로 출력 → COMcopier가 수신)
```

### Step 4. COMcopier 설정

`COMcopier.ConfigUI.exe`를 실행합니다.

1. **설정파일 열기** → 같은 폴더의 `appsettings.json` 선택
2. **매핑 추가** 클릭
3. 매핑 이름 입력 (예: `주문서 분배`)
4. **소스 포트** 설정:
   - 포트: `COM11` (가상 쌍의 반대쪽)
   - 보드레이트: POS 프로그램의 설정과 동일하게 (일반적으로 `9600`)
5. **대상 추가**로 프린터 포트 추가:
   - 포트: `COM3`, `COM4` 등 실제 프린터가 연결된 포트
   - 복사 매수: 원하는 매수
   - 패턴: 특정 문자열이 포함된 주문만 출력하려면 입력 (줄바꿈으로 여러 패턴 입력 가능)
6. **저장** 클릭

#### 패턴 설정 예시

| 대상 포트 | 매수 | 패턴 | 동작 |
|-----------|------|------|------|
| COM3 | 2 | `쿠팡이츠`, `배달의민족` | 배달 주문만 2매 출력 |
| COM4 | 1 | _(비어있음)_ | 모든 주문 1매 출력 |

- 패턴이 **비어있으면** 모든 데이터를 전송합니다.
- 여러 패턴은 **OR 조건**입니다. 하나라도 포함되면 전송합니다.
- 대소문자를 구분하지 않습니다.

### Step 5. 서비스 설치

`install-service.bat`을 **우클릭 → 관리자 권한으로 실행**합니다.

- COMcopier가 Windows 서비스로 등록되고 자동 시작됩니다.
- PC를 재부팅해도 서비스가 자동으로 실행됩니다.

## 설정 변경

1. `COMcopier.ConfigUI.exe`에서 설정 수정 후 저장
2. 서비스 재시작:
   - ConfigUI의 **서비스 재시작** 버튼 사용
   - 또는 관리자 명령 프롬프트에서:
     ```
     sc stop COMcopier
     sc start COMcopier
     ```

## 서비스 제거

`uninstall-service.bat`을 **관리자 권한으로 실행**합니다.

## 설정 파일 직접 편집 (appsettings.json)

ConfigUI 대신 직접 편집할 수도 있습니다.

```json
{
  "COMcopier": {
    "Mappings": [
      {
        "Name": "주문서 분배",
        "Source": {
          "Port": "COM11",
          "BaudRate": 9600,
          "DataBits": 8,
          "Parity": "None",
          "StopBits": "One"
        },
        "Destinations": [
          {
            "Port": "COM3",
            "BaudRate": 9600,
            "Copies": 2,
            "Patterns": ["쿠팡이츠", "배달의민족"],
            "Encoding": "euc-kr"
          },
          {
            "Port": "COM4",
            "BaudRate": 9600,
            "Copies": 1,
            "Patterns": [],
            "Encoding": "euc-kr"
          }
        ]
      }
    ]
  }
}
```

## 트러블슈팅

**가상 포트가 보이지 않는 경우**
- PC를 재부팅했는지 확인
- 장치 관리자 → 포트(COM & LPT)에서 com0com 포트 확인

**서비스가 시작되지 않는 경우**
- 이벤트 뷰어(eventvwr) → Windows 로그 → 응용 프로그램에서 COMcopier 로그 확인
- appsettings.json의 포트 이름이 실제 존재하는 포트인지 확인

**데이터가 전달되지 않는 경우**
- 소스 포트가 가상 쌍의 **반대쪽**(COM11)인지 확인 (POS가 COM10으로 보내면 COMcopier는 COM11에서 수신)
- 보드레이트가 POS 프로그램 설정과 일치하는지 확인
- 패턴 설정이 있다면, 실제 인쇄 데이터에 해당 문자열이 포함되는지 확인

## 개발 환경에서 빌드

**필요 요소:** .NET 8 SDK

```
deploy.bat
```

`deploy/` 폴더에 배포 패키지가 생성됩니다. .NET 런타임이 포함된 self-contained 빌드이므로 POS기에 별도 설치가 필요 없습니다.
