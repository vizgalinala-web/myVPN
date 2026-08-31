import Foundation

public struct RegisterResponse: Codable {
    public let id: UUID
    public let email: String
    public let createdAt: Date
}

public struct CurrentUserResponse: Codable {
    public let id: UUID
    public let email: String
    public let createdAt: Date
}

public struct TokenResponse: Codable {
    public let accessToken: String
    public let refreshToken: String
    public let expiresIn: Int
    public let tokenType: String
}

public struct VpnServerDto: Codable, Identifiable {
    public let id: UUID
    public let name: String
    public let country: String
    public let city: String
    public let hostname: String
    public let endpoint: String
}

public struct ServersResponse: Codable {
    public let servers: [VpnServerDto]
}

public struct DeviceResponse: Codable, Identifiable {
    public let id: UUID
    public let name: String
    public let platform: String
    public let publicKey: String
    public let vpnAddress: String?
    public let lastConnectedServerId: UUID?
    public let connectedAt: Date?
    public let isConnected: Bool
    public let createdAt: Date
    public let lastSeenAt: Date?
    public let isActive: Bool
}

public struct DevicesResponse: Codable {
    public let devices: [DeviceResponse]
}

public struct WireGuardPeerDto: Codable {
    public let publicKey: String
    public let endpoint: String
    public let allowedIps: String
    public let persistentKeepalive: Int
}

public struct DeviceVpnConfigurationResponse: Codable {
    public let deviceId: UUID
    public let serverId: UUID
    public let serverName: String
    public let address: String
    public let dns: String
    public let peer: WireGuardPeerDto
    public let wireGuardQuickConfig: String
}
