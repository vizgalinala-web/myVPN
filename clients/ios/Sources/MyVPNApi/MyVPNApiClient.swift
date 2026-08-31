import Foundation

public enum MyVPNApiError: Error {
    case invalidURL
    case http(Int, Data?)
    case decoding(Error)
}

public final class MyVPNApiClient {
    private let baseURL: URL
    private let session: URLSession
    private var accessToken: String?

    public init(baseURL: URL, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.session = session
    }

    public func setAccessToken(_ token: String?) {
        accessToken = token
    }

    public func register(email: String, password: String) async throws -> RegisterResponse {
        try await post(path: "api/auth/register", body: ["email": email, "password": password])
    }

    public func login(email: String, password: String) async throws -> TokenResponse {
        let tokens: TokenResponse = try await post(path: "api/auth/login", body: ["email": email, "password": password])
        accessToken = tokens.accessToken
        return tokens
    }

    public func me() async throws -> CurrentUserResponse {
        try await get(path: "api/me")
    }

    public func servers() async throws -> ServersResponse {
        try await get(path: "api/servers")
    }

    public func createDevice(name: String, platform: String, publicKey: String) async throws -> DeviceResponse {
        try await post(path: "api/devices", body: [
            "name": name,
            "platform": platform,
            "publicKey": publicKey
        ])
    }

    public func configuration(deviceId: UUID, serverId: UUID) async throws -> DeviceVpnConfigurationResponse {
        try await get(path: "api/devices/\(deviceId.uuidString)/configuration?serverId=\(serverId.uuidString)")
    }

    public func disconnect(deviceId: UUID) async throws {
        let _: Empty = try await post(path: "api/devices/\(deviceId.uuidString)/disconnect", body: [:])
    }

    private struct Empty: Decodable {}

    private func get<T: Decodable>(path: String) async throws -> T {
        guard let url = URL(string: path, relativeTo: baseURL) else { throw MyVPNApiError.invalidURL }
        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        applyAuth(&request)
        return try await send(request)
    }

    private func post<T: Decodable>(path: String, body: [String: String]) async throws -> T {
        guard let url = URL(string: path, relativeTo: baseURL) else { throw MyVPNApiError.invalidURL }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        applyAuth(&request)
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        return try await send(request)
    }

    private func applyAuth(_ request: inout URLRequest) {
        if let accessToken {
            request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        }
    }

    private func send<T: Decodable>(_ request: URLRequest) async throws -> T {
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw MyVPNApiError.http(-1, data)
        }
        guard (200..<300).contains(http.statusCode) else {
            throw MyVPNApiError.http(http.statusCode, data)
        }
        do {
            let decoder = JSONDecoder()
            decoder.dateDecodingStrategy = .iso8601
            return try decoder.decode(T.self, from: data)
        } catch {
            throw MyVPNApiError.decoding(error)
        }
    }
}
